#!/bin/bash
while IFS=$'\t' read -r plex_path jellyfin_filename; do
    echo "Migrating $plex_path to $jellyfin_filename"
    python3 migrate.py "$plex_path" "$jellyfin_filename"
done < missing_files.txt
