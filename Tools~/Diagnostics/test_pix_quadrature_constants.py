"""Validate official export reader ordering and event-time declared constants."""
import struct
import unittest

from ExtractPixQuadratureConstants import ordered_stream,qualify_constants


class PixQuadratureConstantsTests(unittest.TestCase):
    def fixture(self):
        declarations=dict(_SdfDiffuseScreenSize=('float',0,4),_SdfNearOcclusion=('float',16,4),
            _SdfGatherHistoryDiagnostics=('uint',32,1),_SdfDiffuseFrame=('uint',36,1),
            _SdfScreenProbeIntegrationJitter=('uint',40,1),_SdfPreparedAnchorEnabled=('uint',44,1))
        windows={name:bytearray(80) for name in ('native','baseline','stratified')}
        for name,v in windows.items():
            struct.pack_into('<4f',v,0,1920,1080,1/1920,1/1080)
            struct.pack_into('<4f',v,16,.35,0,0,0)
            struct.pack_into('<4I',v,32,32|(67108864 if name=='stratified' else 0),10964,1,1)
        return windows,declarations

    def test_reader_follows_calls_not_file_or_modification_number_order(self):
        funcs=dict(CreateAppResources='\n    Child();\n    g_resourceReader->Read(data, 7);',
            Child='\n    g_resourceReader->Read(data, ResourceModificationSize_003);',
            RenderFrame='\n    Later();',Later='\n    g_resourceReader->Read(data, ResourceModificationSize_001);')
        rows,total=ordered_stream(funcs,{1:5,3:11})
        self.assertEqual([(r['bytes'],r['offset']) for r in rows],[(11,0),(7,11),(5,18)])
        self.assertEqual(total,23)

    def test_reader_rejects_recursive_generated_functions(self):
        with self.assertRaisesRegex(AssertionError,'Recursive'):
            ordered_stream(dict(CreateAppResources='\n    CreateAppResources();',RenderFrame=''),{})

    def test_padding_changes_are_ignored_but_declared_fields_are_verified(self):
        windows,fields=self.fixture();windows['baseline'][63]=99;windows['stratified'][79]=101
        result=qualify_constants(windows,fields,48)
        self.assertTrue(result['constantsQualified'])
        self.assertEqual(result['differentDeclaredFieldsVsNative'],dict(baseline=[],stratified=['_SdfGatherHistoryDiagnostics']))

    def test_wrong_frame_or_extra_candidate_flag_cannot_qualify(self):
        windows,fields=self.fixture();struct.pack_into('<I',windows['baseline'],36,10965)
        result=qualify_constants(windows,fields,48)
        self.assertFalse(result['constantsQualified'])
        self.assertEqual(result['differentDeclaredFieldsVsNative']['baseline'],['_SdfDiffuseFrame'])
        windows,fields=self.fixture();struct.pack_into('<I',windows['stratified'],32,32|67108864|1)
        self.assertFalse(qualify_constants(windows,fields,48)['checks']['expectedFlags'])

    def test_short_window_and_missing_metadata_are_rejected(self):
        windows,fields=self.fixture();windows['native']=windows['native'][:47]
        with self.assertRaises(AssertionError):qualify_constants(windows,fields,48)
        windows,fields=self.fixture();fields['_SdfDiffuseFrame']=('uint',48,1)
        with self.assertRaisesRegex(AssertionError,'outside declared'):qualify_constants(windows,fields,48)
        windows,fields=self.fixture();del fields['_SdfDiffuseFrame']
        with self.assertRaisesRegex(AssertionError,'Missing'):qualify_constants(windows,fields,48)

    def test_wrong_resolution_or_active_ao_is_not_the_retained_fixture(self):
        windows,fields=self.fixture()
        for v in windows.values():struct.pack_into('<f',v,0,1280)
        result=qualify_constants(windows,fields,48)
        self.assertFalse(result['constantsQualified']);self.assertFalse(result['checks']['fullResolution'])
        windows,fields=self.fixture()
        for v in windows.values():struct.pack_into('<f',v,24,1)
        self.assertFalse(qualify_constants(windows,fields,48)['checks']['directionalNearAoDisabled'])


if __name__=='__main__':unittest.main()
