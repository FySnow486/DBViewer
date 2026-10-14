'use strict';
function normalizeSnapshot(input){
    const wrapped=input?.format==='dbviewer.schema-snapshot';
    if(wrapped&&input.version!==1)throw Error('不支持此快照版本。');
    const s=wrapped?input.schema:input;
    const fail=()=>{throw Error('快照格式无效，请选择 DBViewer 导出的结构文件。');};
    const str=v=>typeof v==='string'?v:fail();
    if(!s||!Array.isArray(s.tables)||!Array.isArray(s.relations)||s.tables.length>20000||s.relations.length>100000)fail();
    const tables=s.tables.map(t=>{
        if(!Array.isArray(t.columns)||t.columns.length>10000)fail();
        return {schema:str(t.schema),name:str(t.name),comment:str(t.comment??''),columns:t.columns.map(c=>({name:str(c.name),type:str(c.type),length:c.length==null?null:str(c.length),nullable:!!c.nullable,primaryKey:!!c.primaryKey,defaultValue:c.defaultValue==null?null:str(c.defaultValue),comment:str(c.comment??'')}))};
    });
    const keys=new Map(tables.map(t=>[JSON.stringify([t.schema,t.name]),new Set(t.columns.map(c=>c.name))]));
    if(keys.size!==tables.length)fail();
    const relation=(r,inferred=false)=>{
        const clean=Object.fromEntries(['fromSchema','fromTable','fromColumn','toSchema','toTable','toColumn'].map(k=>[k,str(r[k])]));
        if(!keys.get(JSON.stringify([clean.fromSchema,clean.fromTable]))?.has(clean.fromColumn)||!keys.get(JSON.stringify([clean.toSchema,clean.toTable]))?.has(clean.toColumn))fail();
        return inferred?{...clean,inferred:true,evidence:str(r.evidence??'来自导入快照的业务关联')}:clean;
    };
    const schema={name:str(s.name),provider:str(s.provider),tables,relations:s.relations.map(r=>relation(r))};
    if(wrapped){if(!Array.isArray(input.businessRelations)||input.businessRelations.length>100000)fail();schema.businessRelations=input.businessRelations.map(r=>relation(r,true));}
    return schema;
}
function createSnapshot(schema,businessRelations){
    const normalized=normalizeSnapshot({format:'dbviewer.schema-snapshot',version:1,schema,businessRelations});
    const {businessRelations:links,...clean}=normalized;
    return {format:'dbviewer.schema-snapshot',version:1,exportedAt:new Date().toISOString(),schema:clean,businessRelations:links};
}
const snapshotStore={
    open(){return new Promise((resolve,reject)=>{const r=indexedDB.open('dbviewer-offline',1);r.onupgradeneeded=()=>r.result.createObjectStore('snapshots',{keyPath:'id'});r.onsuccess=()=>resolve(r.result);r.onerror=()=>reject(Error('无法打开本机快照存储。'));});},
    async run(mode,action){const db=await this.open();try{return await new Promise((resolve,reject)=>{const tx=db.transaction('snapshots',mode);const r=action(tx.objectStore('snapshots'));tx.oncomplete=()=>resolve(r.result);tx.onerror=tx.onabort=()=>reject(Error('快照保存失败，请检查浏览器存储空间。'));});}finally{db.close();}},
    list(){return this.run('readonly',s=>s.getAll());},
    save(item){return this.run('readwrite',s=>s.put(item));},
    remove(id){return this.run('readwrite',s=>s.delete(id));}
};
