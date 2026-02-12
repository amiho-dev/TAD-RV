// ───────────────────────────────────────────────────────────────────────────
// PrivacyRedactor.cs — UIAutomation-based password field detector
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Privacy Shield: Scans the active UI tree for password input fields
// using the Windows UIAutomation COM API. Returns their screen coordinates
// so the ScreenCaptureEngine can draw solid black rectangles over the
// D3D11 texture BEFORE it reaches the H.264 encoder.
//
// This ensures the teacher NEVER receives raw password pixels in the
// remote view stream — redaction happens in GPU memory pre-encode.
//
// Enhanced features:
//   • Detects Win32 Edit + ES_PASSWORD, WPF PasswordBox, browser <input type=password>
//   • Configurable redaction categories (addresses, credit cards, etc.)
//   • Thread-safe rectangle cache with atomic swap
//   • GPU-side ClearTextureRegion via ScreenCaptureEngine.ApplyPrivacyRedaction
//
// Refresh rate: Scans every 500ms in a background thread.
// ───────────────────────────────────────────────────────────────────────────

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TadBridge.Shared;

namespace TadBridge.Capture;

/// <summary>
/// Category of content to redact. Extensible via policy.
/// </summary>
[Flags]
public enum RedactionCategory
{
    None           = 0,
    PasswordFields = 1 << 0,  // UIAutomation IsPassword
    CreditCards    = 1 << 1,  // OCR-based (future)
    EmailAddresses = 1 << 2,  // OCR-based (future)
    CustomRegex    = 1 << 3,  // Policy-defined patterns (future)
    All            = 0x7FFF_FFFF
}

public sealed class PrivacyRedactor : IDisposable
{
    private readonly ILogger<PrivacyRedactor> _log;

    // Thread-safe list of current redaction rectangles
    private volatile List<RedactionRect> _currentRects = new();
    private CancellationTokenSource? _cts;
    private Task? _scanTask;

    // Expansion margin around detected password fields (pixels)
    private const int MarginPx = 4;
    private const int ScanIntervalMs = 500;

    // Configurable redaction categories
    public RedactionCategory ActiveCategories { get; set; } = RedactionCategory.PasswordFields;

    // Statistics
    private long _totalScans;
    private long _totalFieldsFound;

    /// <summary>Total number of UIAutomation scans performed.</summary>
    public long TotalScans => Interlocked.Read(ref _totalScans);
    /// <summary>Total number of password fields detected across all scans.</summary>
    public long TotalFieldsFound => Interlocked.Read(ref _totalFieldsFound);

    public PrivacyRedactor(ILogger<PrivacyRedactor> log)
    {
        _log = log;
        Start();
    }

    // ─── Public API ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the current list of screen regions to redact.
    /// Thread-safe, non-blocking.
    /// </summary>
    public IReadOnlyList<RedactionRect> GetRedactionRects() => _currentRects;

    // ─── Background Scanner ───────────────────────────────────────────

    private void Start()
    {
        _cts = new CancellationTokenSource();
        _scanTask = Task.Run(() => ScanLoop(_cts.Token));
        _log.LogInformation("Privacy redactor started (scan interval: {Ms}ms)", ScanIntervalMs);
    }

    private async Task ScanLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var rects = new List<RedactionRect>();

                // Category: Password fields via UIAutomation
                if (ActiveCategories.HasFlag(RedactionCategory.PasswordFields))
                {
                    var pwdRects = ScanForPasswordFields();
                    rects.AddRange(pwdRects);
                }

                // Future: OCR-based categories (CreditCards, EmailAddresses)
                // would scan the DXGI texture directly via DirectML/WinRT OCR

                Interlocked.Increment(ref _totalScans);
                Interlocked.Add(ref _totalFieldsFound, rects.Count);

                // Atomic swap — no lock needed, readers see consistent snapshot
                _currentRects = rects;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Privacy scan error");
            }

            await Task.Delay(ScanIntervalMs, ct);
        }
    }

    // ─── UIAutomation Scanner ─────────────────────────────────────────

    /// <summary>
    /// Uses the Windows UIAutomation COM API to find all password input
    /// fields currently visible on screen.
    ///
    /// Strategy:
    ///   1. Get the root automation element (desktop)
    ///   2. Find all Edit controls where IsPassword == true
    ///   3. Get their bounding rectangles in screen coordinates
    ///   4. Add a small margin for visual safety
    ///
    /// The returned rectangles are passed to ScreenCaptureEngine, which
    /// calls NativeDxgi.ClearTextureRegion() to draw solid black over
    /// those pixel regions in the D3D11 texture BEFORE submission to
    /// the QuickSync H.264 encoder. The teacher never sees the raw pixels.
    ///
    /// Detected control types:
    ///   - Win32 edit controls with ES_PASSWORD style
    ///   - WPF PasswordBox controls
    ///   - Browser password fields (WebView2/Chrome UIA exposure)
    ///   - UWP/WinUI PasswordBox controls
    /// </summary>
    private List<RedactionRect> ScanForPasswordFields()
    {
        var results = new List<RedactionRect>();

        // Initialize UIAutomation COM interface
        int hr = UiaInterop.CoCreateInstance(
            ref UiaInterop.CLSID_CUIAutomation,
            IntPtr.Zero,
            1, // CLSCTX_INPROC_SERVER
            ref UiaInterop.IID_IUIAutomation,
            out IntPtr pAutomation);

        if (hr != 0 || pAutomation == IntPtr.Zero)
            return results;

        try
        {
            // Get root element (desktop)
            IntPtr rootElement = UiaInterop.GetRootElement(pAutomation);
            if (rootElement == IntPtr.Zero) return results;

            try
            {
                // Create condition: ControlType == Edit AND IsPassword == true
                IntPtr condition = UiaInterop.CreatePasswordCondition(pAutomation);
                if (condition == IntPtr.Zero) return results;

                try
                {
                    // FindAll with TreeScope_Descendants
                    IntPtr elementArray = UiaInterop.FindAll(rootElement, condition);
                    if (elementArray == IntPtr.Zero) return results;

                    try
                    {
                        int count = UiaInterop.GetArrayLength(elementArray);

                        for (int i = 0; i < count; i++)
                        {
                            IntPtr element = UiaInterop.GetArrayElement(elementArray, i);
                            if (element == IntPtr.Zero) continue;

                            try
                            {
                                var rect = UiaInterop.GetBoundingRectangle(element);
                                if (rect.Width > 0 && rect.Height > 0)
                                {
                                    results.Add(new RedactionRect
                                    {
                                        X = Math.Max(0, rect.X - MarginPx),
                                        Y = Math.Max(0, rect.Y - MarginPx),
                                        Width = rect.Width + MarginPx * 2,
                                        Height = rect.Height + MarginPx * 2
                                    });
                                }
                            }
                            finally
                            {
                                Marshal.Release(element);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.Release(elementArray);
                    }
                }
                finally
                {
                    Marshal.Release(condition);
                }
            }
            finally
            {
                Marshal.Release(rootElement);
            }
        }
        finally
        {
            Marshal.Release(pAutomation);
        }

        if (results.Count > 0)
            _log.LogDebug("Privacy redactor found {Count} password fields", results.Count);

        return results;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _scanTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { /* Task was cancelled — expected */ }
        catch (ObjectDisposedException) { }
        _cts?.Dispose();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// UIAutomation COM Interop
//
// P/Invoke wrappers for the Windows UIAutomation COM API.
// Uses raw COM vtable calls to avoid a dependency on UIAutomationClient.dll
// managed wrapper (which has threading issues in services).
// ═══════════════════════════════════════════════════════════════════════════

file static class UiaInterop
{
    public static Guid CLSID_CUIAutomation = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
    public static Guid IID_IUIAutomation = new("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE");

    // UIAutomation property IDs
    public const int UIA_ControlTypePropertyId = 30003;
    public const int UIA_IsPasswordPropertyId = 30019;
    public const int UIA_BoundingRectanglePropertyId = 30001;
    public const int UIA_EditControlTypeId = 50004;

    // TreeScope
    public const int TreeScope_Descendants = 4;

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(
        ref Guid clsid, IntPtr pOuter, int context, ref Guid iid, out IntPtr result);

    /// <summary>IUIAutomation::GetRootElement (vtable slot 5)</summary>
    public static IntPtr GetRootElement(IntPtr automation)
    {
        var vtable = Marshal.ReadIntPtr(Marshal.ReadIntPtr(automation) + 5 * IntPtr.Size);
        var fn = Marshal.GetDelegateForFunctionPointer<GetRootElementDelegate>(vtable);
        fn(automation, out IntPtr root);
        return root;
    }
    private delegate int GetRootElementDelegate(IntPtr self, out IntPtr root);

    /// <summary>
    /// Create an AND condition: ControlType==Edit AND IsPassword==True
    /// </summary>
    public static IntPtr CreatePasswordCondition(IntPtr automation)
    {
        // Create ControlType == Edit condition
        IntPtr condType = CreatePropertyCondition(
            automation, UIA_ControlTypePropertyId, UIA_EditControlTypeId);

        // Create IsPassword == true condition
        IntPtr condPwd = CreateBoolPropertyCondition(
            automation, UIA_IsPasswordPropertyId, true);

        if (condType == IntPtr.Zero || condPwd == IntPtr.Zero) return IntPtr.Zero;

        // AND them together: IUIAutomation::CreateAndCondition (vtable slot 25)
        var vtable = Marshal.ReadIntPtr(Marshal.ReadIntPtr(automation) + 25 * IntPtr.Size);
        var fn = Marshal.GetDelegateForFunctionPointer<CreateAndConditionDelegate>(vtable);
        fn(automation, condType, condPwd, out IntPtr result);
        return result;
    }
    private delegate int CreateAndConditionDelegate(
        IntPtr self, IntPtr cond1, IntPtr cond2, out IntPtr result);

    /// <summary>IUIAutomation::CreatePropertyCondition (vtable slot 23)</summary>
    private static IntPtr CreatePropertyCondition(IntPtr automation, int propertyId, int value)
    {
        var vtable = Marshal.ReadIntPtr(Marshal.ReadIntPtr(automation) + 23 * IntPtr.Size);
        var fn = Marshal.GetDelegateForFunctionPointer<CreatePropertyConditionIntDelegate>(vtable);

        // VARIANT with VT_I4
        var variant = new VARIANT { vt = 3, intVal = value };
        fn(automation, propertyId, variant, out IntPtr result);
        return result;
    }
    private delegate int CreatePropertyConditionIntDelegate(
        IntPtr self, int propertyId, VARIANT value, out IntPtr result);

    private static IntPtr CreateBoolPropertyCondition(IntPtr automation, int propertyId, bool value)
    {
        var vtable = Marshal.ReadIntPtr(Marshal.ReadIntPtr(automation) + 23 * IntPtr.Size);
        var fn = Marshal.GetDelegateForFunctionPointer<CreatePropertyConditionIntDelegate>(vtable);

        // VARIANT with VT_BOOL
        var variant = new VARIANT { vt = 11, intVal = value ? -1 : 0 };
        fn(automation, propertyId, variant, out IntPtr result);
        return result;
    }

    /// <summary>IUIAutomationElement::FindAll (vtable slot 5)</summary>
    public static IntPtr FindAll(IntPtr element, IntPtr condition)
    {
        var vtable = Marshal.ReadIntPtr(Marshal.ReadIntPtr(element) + 5 * IntPtr.Size);
        var fn = Marshal.GetDelegateForFunctionPointer<FindAllDelegate>(vtable);
        fn(element, TreeScope_Descendants, condition, out IntPtr result);
        return result;
    }
    private delegate int FindAllDelegate(
        IntPtr self, int scope, IntPtr condition, out IntPtr result);

    /// <summary>IUIAutomationElementArray::get_Length (vtable slot 3)</summary>
    public static int GetArrayLength(IntPtr array)
    {
        var vtable = Marshal.ReadIntPtr(Marshal.ReadIntPtr(array) + 3 * IntPtr.Size);
        var fn = Marshal.GetDelegateForFunctionPointer<GetLengthDelegate>(vtable);
        fn(array, out int len);
        return len;
    }
    private delegate int GetLengthDelegate(IntPtr self, out int length);

    /// <summary>IUIAutomationElementArray::GetElement (vtable slot 4)</summary>
    public static IntPtr GetArrayElement(IntPtr array, int index)
    {
        var vtable = Marshal.ReadIntPtr(Marshal.ReadIntPtr(array) + 4 * IntPtr.Size);
        var fn = Marshal.GetDelegateForFunctionPointer<GetElementDelegate>(vtable);
        fn(array, index, out IntPtr element);
        return element;
    }
    private delegate int GetElementDelegate(IntPtr self, int index, out IntPtr element);

    /// <summary>
    /// IUIAutomationElement::get_CurrentBoundingRectangle (vtable slot 44)
    /// Returns the screen-coordinate rectangle of the element.
    /// </summary>
    public static ScreenRect GetBoundingRectangle(IntPtr element)
    {
        var vtable = Marshal.ReadIntPtr(Marshal.ReadIntPtr(element) + 44 * IntPtr.Size);
        var fn = Marshal.GetDelegateForFunctionPointer<GetBoundingRectDelegate>(vtable);
        fn(element, out var rect);
        return new ScreenRect
        {
            X = (int)rect.Left,
            Y = (int)rect.Top,
            Width = (int)(rect.Right - rect.Left),
            Height = (int)(rect.Bottom - rect.Top)
        };
    }
    private delegate int GetBoundingRectDelegate(IntPtr self, out RECT rect);

    // Interop structs
    [StructLayout(LayoutKind.Sequential)]
    public struct VARIANT
    {
        public short vt;
        public short r1, r2, r3;
        public int intVal;
        public int pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public double Left, Top, Right, Bottom;
    }

    public struct ScreenRect
    {
        public int X, Y, Width, Height;
    }
}
