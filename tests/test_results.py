import copy
import hashlib
from pathlib import Path
import struct
import sys
import unittest

ROOT=Path(__file__).resolve().parent.parent
sys.path.insert(0,str(ROOT/'tools'))
import verify_results as results
from validate_profile import validate


class ResultIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.before=results.load(ROOT/'results/raw'/results.BEFORE)
        self.after=results.load(ROOT/'results/raw'/results.AFTER)
        self.rows=results.load(ROOT/'results/verified-session.json')['results']

    def test_published_data_recomputes(self):
        self.assertEqual(len(results.verify(ROOT)),4)

    def test_changed_gain_is_rejected(self):
        self.rows[2]['after_tflops']=100
        with self.assertRaises(ValueError):results.compare(self.before,self.after,self.rows)

    def test_missing_correctness_is_rejected(self):
        self.after['trials'][0]['benchmarks'][1]['reference_checked']=False
        with self.assertRaises(ValueError):results.compare(self.before,self.after,self.rows)

    def test_unmatched_driver_is_rejected(self):
        self.after['telemetry_at_start']['driver_version']='unmatched'
        with self.assertRaises(ValueError):results.compare(self.before,self.after,self.rows)

    def test_changed_sample_is_rejected(self):
        self.after['trials'][0]['benchmarks'][1]['sample_ms']=[99]*30
        with self.assertRaises(ValueError):results.compare(self.before,self.after,self.rows)


class ProfileValidationTests(unittest.TestCase):
    @staticmethod
    def fixture(identity=0x248910de):
        data=bytearray(144)
        struct.pack_into('<8sIIIII',data,0,b'GUPROF17',1,176,identity,0x248910de,0xb74000a1)
        return bytes(data)+hashlib.sha256(data).digest()

    def test_valid_format_is_not_hardware_approval(self):
        self.assertFalse(validate(self.fixture())['hardware_compatibility_approved'])

    def test_wrong_target_is_rejected(self):
        with self.assertRaises(ValueError):validate(self.fixture(0x220310de))

    def test_corrupt_profile_is_rejected(self):
        data=bytearray(self.fixture());data[100]^=1
        with self.assertRaises(ValueError):validate(data)

    def test_truncated_profile_is_rejected(self):
        with self.assertRaises(ValueError):validate(self.fixture()[:-1])


if __name__=='__main__':unittest.main()
