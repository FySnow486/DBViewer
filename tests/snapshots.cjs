const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const ctx=vm.createContext({});vm.runInContext(fs.readFileSync(require('node:path').join(__dirname,'../wwwroot/snapshots.js'),'utf8'),ctx);
const source={name:'离线测试',provider:'sqlserver',connectionString:'SECRET',tables:[{schema:'dbo',name:'A',comment:'多行\n注释',columns:[{name:'id',type:'bigint',primaryKey:true,nullable:false,comment:'主键'},{name:'parent',type:'bigint',comment:'关联'}]}],relations:[{fromSchema:'dbo',fromTable:'A',fromColumn:'parent',toSchema:'dbo',toTable:'A',toColumn:'id'}]};
const business=[{...source.relations[0],inferred:true,evidence:'注释依据'}];
const snapshot=ctx.createSnapshot(source,business);const restored=ctx.normalizeSnapshot(JSON.parse(JSON.stringify(snapshot)));
assert.equal(restored.tables[0].comment,'多行\n注释');assert.equal(restored.relations.length,1);assert.equal(restored.businessRelations[0].evidence,'注释依据');assert(!JSON.stringify(snapshot).includes('SECRET'));assert.equal(snapshot.format,'dbviewer.schema-snapshot');
assert.equal(ctx.normalizeSnapshot(source).tables.length,1);
for(const invalid of [{}, {...snapshot,version:9},{...snapshot,schema:{...source,tables:[]}},{...snapshot,businessRelations:[{...business[0],toColumn:'missing'}]}])assert.throws(()=>ctx.normalizeSnapshot(invalid));
console.log('PASS: snapshot roundtrip, legacy import, Unicode comments, saved business relations, credentials excluded, invalid versions and missing endpoints rejected');
