"""Validate the local v17 runtime profile format; not a compatibility approval."""
import argparse
import hashlib
from pathlib import Path
import struct


def validate(data):
    if len(data) != 176:
        raise ValueError('Expected exactly 176 bytes')
    if data[:8] != b'GUPROF17' or struct.unpack_from('<II', data, 8) != (1, 176):
        raise ValueError('Unknown runtime profile format')
    if hashlib.sha256(data[:144]).digest() != data[144:]:
        raise ValueError('Runtime profile checksum mismatch')
    identity, subsystem, chip = struct.unpack_from('<III', data, 16)
    if identity != 0x248910de or subsystem != 0x248910de or chip != 0xb74000a1:
        raise ValueError('This source port is restricted to the captured Palit 2489 target')
    return {'format': 'GUPROF17', 'bytes': len(data), 'checksum_valid': True,
            'hardware_compatibility_approved': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('profile', type=Path)
    args = parser.parse_args()
    try:
        validate(args.profile.read_bytes())
        print('Profile format, checksum and target match. Per-PC compatibility remains unverified.')
    except (ValueError, OSError) as error:
        parser.exit(1, f'Profile refused: {error}\n')
