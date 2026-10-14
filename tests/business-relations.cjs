const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const ctx=vm.createContext({});
vm.runInContext(fs.readFileSync(require('node:path').join(__dirname,'../wwwroot/business-relations.js'),'utf8'),ctx);
const col=(name,comment='',primaryKey=false)=>({name,comment,primaryKey});
const table=(schema,name,columns)=>({schema,name,columns});
const source=table('dbo','Child',[col('parent_id','父记录（→ PARENT.ID）'),col('owner_id','关联 Parent.id'),col('self_id','父节点，逻辑自关联'),col('missing_id','→ MISSING_TABLE.id'),col('prose','关联编号ID，不是表引用'),col('composite_id','→ Composite'),col('bad_column','→ Parent.missing'),col('id','',true)]);
const parent=table('dbo','Parent',[col('id','',true)]);
const declared={fromSchema:'dbo',fromTable:'Child',fromColumn:'parent_id',toSchema:'dbo',toTable:'Parent',toColumn:'id'};
const schema={tables:[source,parent,table('other','Parent',[col('id','',true)]),table('dbo','Composite',[col('a','',true),col('b','',true)])],relations:[declared]};
const result=ctx.inferBusinessRelations(schema);
assert.equal(result.relations.length,2); // Owner plus explicit self reference; declared FK deduplicated.
assert.equal(result.relations.find(r=>r.fromColumn==='owner_id').toSchema,'dbo');
assert.equal(result.relations.find(r=>r.fromColumn==='self_id').toTable,'Child');
assert(result.relations.every(r=>r.inferred&&r.evidence));
assert.equal(result.unresolved.length,3); // Missing table/column and unspecified composite key.
assert.equal(schema.relations.length,1); // No database metadata mutation.
console.log('PASS: comment references, schema ambiguity, self reference, existing FK deduplication, missing targets, composite keys and unchanged database metadata');

const qc={tables:[table('dbo','DEMO_QC_ITEM',[{...col('main_id','检查记录 ID'),type:'bigint'}]),table('dbo','DEMO_QC_MAIN',[{...col('id','',true),type:'bigint'},{...col('template_id','关联检查模板 ID'),type:'bigint'}]),table('dbo','DEMO_QC_TEMPLATE',[{...col('id','',true),type:'bigint'}]),table('dbo','DEMO_QC_TEMPLATE_ITEM',[{...col('template_id','模板 ID'),type:'bigint'}])],relations:[]};
const qcResult=ctx.inferBusinessRelations(qc);assert.equal(qcResult.relations.length,3);
assert(qcResult.relations.some(r=>r.fromTable==='DEMO_QC_ITEM'&&r.toTable==='DEMO_QC_MAIN'));
assert(qcResult.relations.some(r=>r.fromTable==='DEMO_QC_MAIN'&&r.toTable==='DEMO_QC_TEMPLATE'));
assert(qcResult.relations.some(r=>r.fromTable==='DEMO_QC_TEMPLATE_ITEM'&&r.toTable==='DEMO_QC_TEMPLATE'));
qc.tables[0].columns[0].type='nvarchar';assert.equal(ctx.inferBusinessRelations(qc).relations.length,2);
console.log('PASS: generic record/item/template links and incompatible types skipped');
