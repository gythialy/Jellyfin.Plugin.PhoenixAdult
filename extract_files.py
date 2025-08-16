import os

def main():
    network_files = []
    site_files = []
    with open('plex_plugin_contents.txt', 'r') as f:
        for line in f:
            if 'Contents/Code/network' in line:
                network_files.append(line)
            elif 'Contents/Code/site' in line:
                site_files.append(line)

    with open('plex_network_files.txt', 'w') as f:
        f.writelines(network_files)

    with open('plex_site_files.txt', 'w') as f:
        f.writelines(site_files)

    print(f"Found {len(network_files)} network files and {len(site_files)} site files.")

if __name__ == "__main__":
    main()
