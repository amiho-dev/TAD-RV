// ───────────────────────────────────────────────────────────────────────────
// TadProtocol.cs — Wire protocol for Teacher ↔ Student TCP communication
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Shared between TadTeacher (client) and TadBridgeService (listener).
// Binary framing: [4-byte length][1-byte command][payload]
// ───────────────────────────────────────────────────────────────────────────

using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace TadBridge.Shared;

// ═══════════════════════════════════════════════════════════════════════════
// Command Types
// ═══════════════════════════════════════════════════════════════════════════

public enum TadCommand : byte
{
    // Teacher → Student
    Ping            = 0x01,
    Lock            = 0x10,
    Unlock          = 0x11,
    Freeze          = 0x12,     // Freeze screen with message overlay
    Unfreeze        = 0x13,     // Remove freeze overlay
    BlankScreen     = 0x14,     // Black screen with "Eyes on Teacher"
    UnblankScreen   = 0x15,     // Restore screen
    RvStart         = 0x20,     // Start remote view streaming (sub-stream)
    RvStop          = 0x21,     // Stop remote view streaming
    RvFocusStart    = 0x22,     // Start main-stream (30fps 720p) for focused view
    RvFocusStop     = 0x23,     // Stop main-stream (keep sub-stream running)
    CollectFiles    = 0x30,     // Request file collection from student
    PushMessage     = 0x40,     // Display a message on student screen
    ChatMessage     = 0x41,     // Teacher → Student chat message
    LaunchApp       = 0x42,     // Launch application on student
    LaunchUrl       = 0x43,     // Open URL on student browser

    // Student → Teacher
    Pong            = 0x81,
    Status          = 0x82,     // JSON status payload
    HandRaise       = 0x83,     // Student requests help
    HandLower       = 0x84,     // Student cancels help request
    ChatReply       = 0x85,     // Student → Teacher chat reply
    VideoFrame      = 0xA0,     // H.264 sub-stream frame (1fps 480p)
    VideoKeyFrame   = 0xA1,     // H.264 sub-stream IDR
    MainFrame       = 0xA2,     // H.264 main-stream frame (30fps 720p)
    MainKeyFrame    = 0xA3,     // H.264 main-stream IDR
    FileChunk       = 0xB0,     // File transfer chunk
    FileComplete    = 0xB1,     // File transfer done
    Error           = 0xFF,
}

// ═══════════════════════════════════════════════════════════════════════════
// Status Payload (JSON-serialized)
// ═══════════════════════════════════════════════════════════════════════════

public sealed class StudentStatus
{
    public string Hostname { get; set; } = "";
    public string Username { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public bool DriverLoaded { get; set; }
    public bool IsLocked { get; set; }
    public bool IsStreaming { get; set; }
    public bool IsFrozen { get; set; }
    public int FreezeSecondsRemaining { get; set; }
    public bool IsHandRaised { get; set; }
    public bool IsBlankScreen { get; set; }
    public string ActiveWindow { get; set; } = "";
    public double CpuUsage { get; set; }
    public long RamUsedMb { get; set; }
    public string Role { get; set; } = "Student";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// ═══════════════════════════════════════════════════════════════════════════
// File Collection Request
// ═══════════════════════════════════════════════════════════════════════════

public sealed class CollectFilesRequest
{
    public string SourcePath { get; set; } = @"C:\Users\%USERNAME%\Documents\Homework";
    public string FilePattern { get; set; } = "*.*";
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024; // 50 MB
}

// ═══════════════════════════════════════════════════════════════════════════
// Frame Codec — Length-prefixed binary framing
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Wire format:
///   [4 bytes: big-endian payload length (cmd + data)]
///   [1 byte:  TadCommand]
///   [N bytes: payload data]
///
/// Max frame size: 16 MB (sanity limit for video frames)
/// </summary>
public static class TadFrameCodec
{
    public const int HeaderSize = 5;     // 4 (length) + 1 (command)
    public const int MaxPayload = 16 * 1024 * 1024;

    /// <summary>
    /// Encode a command + payload into a wire frame.
    /// </summary>
    public static byte[] Encode(TadCommand cmd, ReadOnlySpan<byte> payload = default)
    {
        int totalPayload = 1 + payload.Length; // cmd byte + data
        var frame = new byte[4 + totalPayload];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, 4), totalPayload);
        frame[4] = (byte)cmd;
        if (payload.Length > 0)
            payload.CopyTo(frame.AsSpan(5));
        return frame;
    }

    /// <summary>
    /// Encode a command with a JSON-serialized object payload.
    /// </summary>
    public static byte[] EncodeJson<T>(TadCommand cmd, T obj)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(obj);
        return Encode(cmd, json);
    }

    /// <summary>
    /// Try to parse a complete frame from a buffer.
    /// Returns true if a complete frame was found; advances the buffer position.
    /// </summary>
    public static bool TryDecode(
        ReadOnlySpan<byte> buffer,
        out TadCommand command,
        out ReadOnlyMemory<byte> payload,
        out int bytesConsumed)
    {
        command = 0;
        payload = ReadOnlyMemory<byte>.Empty;
        bytesConsumed = 0;

        if (buffer.Length < 4)
            return false;

        int payloadLen = BinaryPrimitives.ReadInt32BigEndian(buffer);
        if (payloadLen < 1 || payloadLen > MaxPayload)
        {
            // Protocol error — skip 4 bytes
            bytesConsumed = 4;
            return false;
        }

        int totalFrame = 4 + payloadLen;
        if (buffer.Length < totalFrame)
            return false; // Need more data

        command = (TadCommand)buffer[4];
        if (payloadLen > 1)
        {
            payload = buffer.Slice(5, payloadLen - 1).ToArray();
        }
        bytesConsumed = totalFrame;
        return true;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Privacy Redaction Rectangle
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Coordinates of a password field to black out before streaming.
/// Sent alongside VideoFrame metadata.
/// </summary>
public struct RedactionRect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
