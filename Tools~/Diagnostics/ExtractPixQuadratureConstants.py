"""Extract event-time b0 and actual shader from an official PIX C++ export."""
import argparse
import ctypes as c
import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess


def decompress(raw):
    cab=c.WinDLL('cabinet.dll',use_last_error=True)
    cab.CreateDecompressor.argtypes=[c.c_uint,c.c_void_p,c.POINTER(c.c_void_p)]
    cab.Decompress.argtypes=[c.c_void_p,c.c_void_p,c.c_size_t,c.c_void_p,c.c_size_t,c.POINTER(c.c_size_t)]
    cab.CloseDecompressor.argtypes=[c.c_void_p]
    handle=c.c_void_p();assert cab.CreateDecompressor(3,None,c.byref(handle))
    try:
        src=c.create_string_buffer(raw);size=c.c_size_t()
        assert not cab.Decompress(handle,src,len(raw),None,0,c.byref(size)) and c.get_last_error()==122
        dst=c.create_string_buffer(size.value);actual=c.c_size_t()
        assert cab.Decompress(handle,src,len(raw),dst,size.value,c.byref(actual)) and actual.value==size.value
        return dst.raw[:actual.value]
    finally:assert cab.CloseDecompressor(handle)


def functions(export):
    result={}
    for path in sorted(export.glob('*.cpp')):
        text=path.read_text();marks=list(re.finditer(r'^void (\w+)\(\)\s*\{',text,re.M))
        for i,mark in enumerate(marks):
            assert mark[1] not in result
            result[mark[1]]=text[mark.end():marks[i+1].start() if i+1<len(marks) else len(text)]
    return result


def ordered_stream(funcs,sizes):
    result=[]
    def expand(name,trail):
        assert name not in trail,'Recursive generated reader'
        for token in re.finditer(r'g_resourceReader->Read\(\w+, (\w+)\);|^\s+(\w+)\(\);',funcs[name],re.M):
            if token[1]:
                value=token[1];n=int(value) if value.isdecimal() else sizes[int(value.rsplit('_',1)[1])]
                result.append(dict(function=name,value=value,bytes=n))
            elif token[2] in funcs:expand(token[2],(*trail,name))
    for entry in ('CreateAppResources','RenderFrame'):expand(entry,())
    cursor=0
    for row in result:row['offset']=cursor;cursor+=row['bytes']
    return result,cursor


def qualify_constants(windows,declarations,buffer_size):
    """Compare declared fields only; captured upload padding has no shader meaning."""
    assert set(windows)=={'native','baseline','stratified'}
    assert 0<buffer_size<=min(map(len,windows.values()))
    for _,offset,count in declarations.values():
        assert offset>=0 and count>0 and offset+count*4<=buffer_size,'Field outside declared cbuffer'
    different={name:[field for field,(_,offset,count) in declarations.items()
        if windows['native'][offset:offset+count*4]!=windows[name][offset:offset+count*4]]
        for name in ('baseline','stratified')}
    required={'_SdfDiffuseScreenSize','_SdfGatherHistoryDiagnostics','_SdfScreenProbeIntegrationJitter',
              '_SdfPreparedAnchorEnabled','_SdfNearOcclusion','_SdfDiffuseFrame'}
    assert required.issubset(declarations),'Missing Resolve constant declaration'
    def values(name,stage):
        typ,offset,count=declarations[name]
        return struct.unpack_from('<'+str(count)+{'float':'f','int':'i','uint':'I'}[typ],windows[stage],offset)
    flags={stage:values('_SdfGatherHistoryDiagnostics',stage)[0] for stage in windows}
    checks=dict(baselineDeclaredFieldsExact=not different['baseline'],
        candidateOnlyEnableFlagDiffers=different['stratified']==['_SdfGatherHistoryDiagnostics'],
        expectedFlags=flags==dict(native=32,baseline=32,stratified=32|67108864),
        fullResolution=all(values('_SdfDiffuseScreenSize',s)[:2]==(1920.0,1080.0) for s in windows),
        jitterEnabled=all(values('_SdfScreenProbeIntegrationJitter',s)==(1,) for s in windows),
        preparedAnchorEnabled=all(values('_SdfPreparedAnchorEnabled',s)==(1,) for s in windows),
        directionalNearAoDisabled=all(values('_SdfNearOcclusion',s)[2]==0 for s in windows))
    return dict(constantsQualified=all(checks.values()),checks=checks,
                differentDeclaredFieldsVsNative=different)


def extract(export,output,expected_shader_hash,dxc,layout):
    output.mkdir(parents=True,exist_ok=False)
    payload=(export/'resources.bin').read_bytes();funcs=functions(export)
    header=(export/'ResourceModifications.h').read_text()
    sizes={int(i):int(n) for i,n in re.findall(r'ResourceModificationSize_(\d+)\s*=\s*(\d+)',header)}
    stream,total=ordered_stream(funcs,sizes);assert total==len(payload),'Reader stream does not cover payload exactly'
    (output/'ordered-reader.json').write_text(json.dumps(stream,indent=2)+'\n')
    commands='\n'.join(p.read_text() for p in sorted(export.glob('CommandLists_*.cpp')))
    worker='\n'.join(p.read_text() for p in sorted(export.glob('RenderFrameWorker_*.cpp')))
    stages={};windows={};decoded={}
    for label in ('Screen Probe Resolve','GI Quadrature Baseline Resolve','GI Quadrature Stratified Resolve'):
        marker='LR"('+label+')"';assert commands.count(marker)==1
        start=commands.index(marker);match=re.search(r'->Dispatch\(240, 135, 1\);',commands[start:]);assert match
        snippet=commands[start:start+match.end()]
        pso=int(re.search(r'SetPipelineState\(GetPipelineState\((\d+)\)\)',snippet)[1])
        rid,offset=map(int,re.search(r'SetComputeRootConstantBufferView\(0, GetGpuva\((\d+), (\d+)\)\)',snippet).groups())
        owner=list(re.finditer(r'^void (PopulateCommandList_\w+)\(\)',commands[:start],re.M))[-1][1]
        cut=worker.index(owner+'();');prior=list(map(int,re.findall(r'ModifyResource_(\d+)_\d+\(',worker[:cut])))
        pages_by_mod={}
        for modification in sizes:
            m=re.search(r'PagesIndex_'+str(rid)+'_'+str(modification)+r'\[\d+\]\s*=\s*\{(.*?)\};',header,re.S)
            if m:pages_by_mod[modification]=list(map(int,re.findall(r'\d+',m[1])))
        window=bytearray();current=offset;remain=1280;used=[]
        while remain:
            page,inside=divmod(current,4096);take=min(remain,4096-inside)
            applicable=[m for m in prior if page in pages_by_mod.get(m,[])]
            assert applicable,('No preceding update',rid,page)
            mod=applicable[-1];used.append(mod)
            assert not any(page in pages_by_mod.get(m,[]) for m in sizes if m>max(prior)),'Later update aliases constant page'
            if mod not in decoded:
                rows=[v for v in stream if v['value']=='ResourceModificationSize_'+str(mod).zfill(3)];assert len(rows)==1
                pos=rows[0]['offset'];raw=payload[pos:pos+sizes[mod]];data=decompress(raw)
                assert len(data)==len(pages_by_mod[mod])*4096
                decoded[mod]=data
                (output/('modification-'+str(mod)+'.xpress')).write_bytes(raw)
                (output/('modification-'+str(mod)+'.bin')).write_bytes(data)
            data=decoded[mod];index=pages_by_mod[mod].index(page)
            window.extend(data[index*4096+inside:index*4096+inside+take]);current+=take;remain-=take
        name='native' if label=='Screen Probe Resolve' else 'baseline' if 'Baseline' in label else 'stratified'
        windows[name]=bytes(window);(output/(name+'-b0.bin')).write_bytes(window)
        (output/(name+'-calls.cpp.txt')).write_text(snippet)
        stages[name]=dict(label=label,pso=pso,resource=rid,offset=offset,windowBytes=len(window),
                          modifications=sorted(set(used)),windowSha256=hashlib.sha256(window).hexdigest())
    assert len({s['pso'] for s in stages.values()})==1,'Three draws used different PSOs'
    pso=stages['native']['pso'];function='CreateComputePipelineState_'+str(pso)
    read=[r for r in stream if r['function']==function];assert len(read)==1
    row=read[0];compressed=payload[row['offset']:row['offset']+row['bytes']];shader=decompress(compressed)
    expected=int(re.search(r'cpsoDesc.CS = \{ reinterpret_cast<BYTE\*>\(&data\[offset\]\), (\d+) \}',funcs[function])[1])
    assert len(shader)==expected and hashlib.sha256(shader).hexdigest()==expected_shader_hash
    (output/'shader.xpress').write_bytes(compressed);(output/'shader.dxbc').write_bytes(shader)
    text=subprocess.check_output([str(dxc),'-dumpbin',str(output/'shader.dxbc')],text=True)
    (output/'shader.asm.txt').write_text(text)
    metadata=json.loads(layout.read_text(encoding='utf-8-sig'))
    assert metadata['success'] and metadata['data']['result']['success']
    metadata=metadata['data']['result']['result'];assert metadata['shaderHash']==expected_shader_hash
    buffers=[b for b in metadata['constantBuffers'] if b['Name']=='$Globals'];assert len(buffers)==1
    buffer=buffers[0];assert buffer['Size']<=1280
    declarations={}
    for field in buffer['Fields']:
        assert field['ArraySize']==0 and field['StructSize']==0
        scalar={0:'float',4:'int',5:'uint'}[field['DataType']]
        count=field['Rows']*field['Columns'];declarations[field['Name']]=(scalar,field['Index'],count)
    keys=[n for n in declarations if n in ('_SdfDiffuseScreenSize','_SdfGatherHistoryDiagnostics','_SdfScreenProbeIntegrationJitter','_SdfProbeAnchorMaxAge','_SdfPreparedAnchorEnabled','_SdfNearOcclusion') or 'Frame' in n]
    assert '_SdfDiffuseScreenSize' in keys and '_SdfGatherHistoryDiagnostics' in keys
    fields={}
    for key in keys:
        typ,offset,count=declarations[key]
        fmt='<'+str(count)+('I' if typ.startswith('uint') else 'i' if typ.startswith('int') else 'f')
        fields[key]=dict(type=typ,offset=offset,values={name:list(struct.unpack_from(fmt,window,offset)) for name,window in windows.items()})
    qualification=qualify_constants(windows,declarations,buffer['Size'])
    result=dict(schema='pix-quadrature-event-constants.v1',resourcesBytes=len(payload),resourcesSha256=hashlib.sha256(payload).hexdigest(),
                readerStreamComplete=True,stages=stages,shaderSha256=expected_shader_hash,shaderMatchesLiveCompiler=True,fields=fields,
                constantBufferSize=buffer['Size'],layoutSha256=hashlib.sha256(layout.read_bytes()).hexdigest(),**qualification,
                note='1280-byte b0 windows include padding; only declared selected fields are interpreted. Compressed page updates precede target command-list population; later updates do not alias these pages.')
    (output/'extraction.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2))
    return result


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('export',type=Path);p.add_argument('--output',type=Path,required=True)
    p.add_argument('--shader-hash',required=True);p.add_argument('--dxc',type=Path,default=Path('D:/Scoop/apps/vulkan/current/Bin/dxc.exe'))
    p.add_argument('--layout',type=Path,required=True)
    args=p.parse_args();extract(args.export.resolve(),args.output.resolve(),args.shader_hash,args.dxc,args.layout.resolve())
