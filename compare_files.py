import os

def get_plex_files(filename):
    files = []
    with open(filename, 'r') as f:
        for line in f:
            parts = line.strip().split()
            if len(parts) > 3:
                files.append(parts[-1])
    return files

def get_jellyfin_files(filename):
    files = []
    with open(filename, 'r') as f:
        for line in f:
            files.append(line.strip())
    return files

def convert_plex_to_jellyfin(plex_filename):
    basename = os.path.basename(plex_filename)
    name, _ = os.path.splitext(basename)
    if name.startswith('network'):
        name = name.replace('network', 'Network')
    elif name.startswith('site'):
        name = name.replace('site', 'Site')

    # some plex files have different names, we need to manually map them
    if name == "Network5Kporn":
        name = "Network5kporn"
    if name == "NetworkMYLF":
        name = "NetworkMylf"

    return f"{name}.cs"

def main():
    plex_network_files = get_plex_files('plex_network_files.txt')
    plex_site_files = get_plex_files('plex_site_files.txt')
    plex_files = plex_network_files + plex_site_files

    jellyfin_files_raw = get_jellyfin_files('jellyfin_site_files.txt')
    jellyfin_files = [os.path.basename(f) for f in jellyfin_files_raw]

    missing_files = []
    for plex_file in plex_files:
        jellyfin_filename = convert_plex_to_jellyfin(plex_file)
        if jellyfin_filename not in jellyfin_files:
            missing_files.append({'plex_path': plex_file, 'jellyfin_filename': jellyfin_filename})

    with open('missing_files.txt', 'w') as f:
        for file_info in missing_files:
            f.write(f"{file_info['plex_path']}\t{file_info['jellyfin_filename']}\n")

    print(f"Found {len(missing_files)} missing files.")

if __name__ == "__main__":
    main()
