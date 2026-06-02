import os
import re

def rename_tad_in_content(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
    except:
        return
        
    # Replace "TAD" with "TAD"
    new_content = re.sub(r'TAD', 'TAD', content)
    
    # Replace versions
    # Version/FileVersion like 26.62.BF or 26.62.BF.600
    new_content = re.sub(r'<Version>26\.[^<]+</Version>', '<Version>26.62.0</Version>', new_content)
    new_content = re.sub(r'<FileVersion>26\.[^<]+</FileVersion>', '<FileVersion>26.62.0</FileVersion>', new_content)
    new_content = re.sub(r'<InformationalVersion>v26\.[^<]+-(.+?)</InformationalVersion>', r'<InformationalVersion>v26.62.BF-\1</InformationalVersion>', new_content)
    new_content = new_content.replace('26.62.BF', '26.62.BF')
    
    if content != new_content:
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(new_content)

def rename_files_and_directories():
    for root, dirs, files in os.walk('/workspaces/TAD-RV', topdown=False):
        if '.git' in root or 'obj' in root or 'bin' in root or 'node_modules' in root:
            continue
            
        for name in files:
            filepath = os.path.join(root, name)
            rename_tad_in_content(filepath)
            
            if 'TAD' in name:
                new_name = name.replace('TAD', 'TAD')
                os.rename(filepath, os.path.join(root, new_name))
                
        for name in dirs:
            if 'TAD' in name:
                os.rename(os.path.join(root, name), os.path.join(root, name.replace('TAD', 'TAD')))

if __name__ == '__main__':
    rename_files_and_directories()
