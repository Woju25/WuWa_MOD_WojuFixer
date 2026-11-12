import os

def fix_color_buf(file_path):
    # 🔹 Read original first
    with open(file_path, "rb") as f:
        original = f.read()

    data = bytearray(original)

    # 🔹 ALWAYS create backup from ORIGINAL (not modified)
    backup_path = file_path + ".bak"
    if not os.path.exists(backup_path):
        with open(backup_path, "wb") as f:
            f.write(original)
        print(f"Backup created: {backup_path}")

    new_color = bytes([0xFF, 0xBC, 0xBC, 0x33])

    for i in range(0, len(data), 4):
        r, g, b, a = data[i:i+4]
        if r == 255 and g == 255 and b == 255 and a == 255:
            data[i:i+4] = new_color

    with open(file_path, "wb") as f:
        f.write(data)

    print(f"Fixed Color.buf: {file_path}")


def fix_texcoord_buf(file_path):
    # 🔹 Read original first
    with open(file_path, "rb") as f:
        original = f.read()

    data = bytearray(original)

    # 🔹 ALWAYS create backup
    backup_path = file_path + ".bak"
    if not os.path.exists(backup_path):
        with open(backup_path, "wb") as f:
            f.write(original)
        print(f"Backup created: {backup_path}")

    zero = bytes([0, 0, 0, 0])

    for i in range(0, len(data), 4):
        r, g, b, a = data[i:i+4]
        if r == 255 and g == 255 and b == 255 and a == 255:
            data[i:i+4] = zero

    with open(file_path, "wb") as f:
        f.write(data)

    print(f"Fixed TexCoord.buf (COLOR1): {file_path}")


def scan_folder(folder):
    for root, _, files in os.walk(folder):
        for f in files:
            path = os.path.join(root, f)

            if f.lower() == "color.buf":
                fix_color_buf(path)

            elif f.lower() == "texcoord.buf":
                fix_texcoord_buf(path)


import sys

if __name__ == "__main__":
    if len(sys.argv) > 1:
        scan_folder(sys.argv[1])
    else:
        print("No folder provided")