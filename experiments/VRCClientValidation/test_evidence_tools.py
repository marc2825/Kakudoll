"""復号器/記録checkerの負対照。合成入力はVRChat観測結果ではない。"""
import copy
import hashlib
import json
from pathlib import Path
import struct
import tempfile
import unittest
import zlib

import check_client_evidence
import decode_card


def png(words, flip=False):
    width,height=272,512; rows=[]
    for y in range(height):
        row=bytearray()
        for x in range(width):
            col=(width-1-x if flip else x)//8-1; index=y//8-8
            bit=(words[index] >> col)&1 if 0<=col<32 and 0<=index<56 else 0
            row.extend([255*bit]*3)
        rows.append(b'\x00'+row)
    def chunk(kind, data): return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
    return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(b''.join(rows)))+chunk(b'IEND',b'')


class EvidenceToolsTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(); self.folder=Path(self.temp.name)
        self.words=[0]*56; self.words[:5]=[0x564c4331,1,2,0,2]
        self.words[10]=struct.unpack('<I',struct.pack('<f',5))[0] # deliberately invalid strict path
        value=2166136261
        for word in self.words[:55]:value=((value^word)*16777619)&0xffffffff
        self.words[55]=value

    def tearDown(self): self.temp.cleanup()
    def write(self,data):
        path=self.folder/'input.png';path.write_bytes(data);return path

    def test_valid_payload_remains_unassessed(self):
        value=decode_card.decode(self.write(png(self.words)))
        self.assertEqual(value['status'],5);self.assertIn('UNASSESSED',value['acceptance_status'])

    def test_mirror_orientation(self):
        value=decode_card.decode(self.write(png(self.words,True)))
        self.assertTrue(value['orientation']['reflected']);self.assertEqual(value['words'],self.words)

    def test_projective_corner_math(self):
        corners=[(30,10),(290,50),(250,500),(10,450)]; h=decode_card.homography(corners)
        for uv,xy in zip(((0,0),(1,0),(1,1),(0,1)),corners):
            self.assertAlmostEqual(h(*uv)[0],xy[0]);self.assertAlmostEqual(h(*uv)[1],xy[1])

    def test_bad_png_crc_rejected(self):
        data=bytearray(png(self.words));data[29]^=1
        with self.assertRaises(ValueError):decode_card.decode(self.write(data))

    def test_payload_bit_corruption_rejected(self):
        self.words[14]^=1
        with self.assertRaises(ValueError):decode_card.decode(self.write(png(self.words)))

    def test_invalid_raw_global_preserved(self):
        self.words[24]=0x7fc00000 # NaN mirror raw position, with strict status=5
        value=2166136261
        for word in self.words[:55]:value=((value^word)*16777619)&0xffffffff
        self.words[55]=value
        decoded=decode_card.decode(self.write(png(self.words)))
        self.assertEqual(decoded['nonfinite_float_word_indices'],[24]);self.assertEqual(decoded['mirror_position'][0],'nan')

    def record(self):
        return json.loads((Path(__file__).parent/'client-results-template.json').read_text())

    def inspect(self,data):
        path=self.folder/'results.json';path.write_text(json.dumps(data));return check_client_evidence.inspect(path)

    def test_pending_record(self): self.assertEqual(self.inspect(self.record()),{'NOT RUN':10})

    def test_missing_evidence_cannot_pass(self):
        data=self.record();data['tests'][0]['status']='PASS'
        with self.assertRaises(ValueError):self.inspect(data)

    def test_simulation_cannot_be_client_pass(self):
        data=self.record();test=data['tests'][0];test['status']='PASS'
        test['provenance']=dict(client_version='synthetic',session_label='test',capture_method='test',human_operator_attestation='test',source='UnityEditorPreflight')
        with self.assertRaisesRegex(ValueError,'simulated'):self.inspect(data)

    def test_local_avatar_cannot_pass_two_observers(self):
        data=self.record();test=data['tests'][7];test['status']='PASS';test['checks']=dict.fromkeys(test['checks'],True)
        test['provenance']=dict(client_version='synthetic-unit-test',session_label='unit-test',capture_method='unit-test',human_operator_attestation='unit-test',source='VRChatClient',avatar_distribution='BuildAndTest')
        path=self.folder/'fake-evidence.txt';path.write_text('unit-test only, not an actual client observation')
        test['evidence']=[dict(path=path.name,sha256=hashlib.sha256(path.read_bytes()).hexdigest())]
        with self.assertRaisesRegex(ValueError,'local-only'):self.inspect(data)


if __name__=='__main__':unittest.main()
