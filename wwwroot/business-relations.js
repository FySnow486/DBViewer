'use strict';
// Infer only explicit metadata references; these never become database constraints.
function inferBusinessRelations(schema){
    const relations=[],unresolved=[],seen=new Set();
    const signature=r=>JSON.stringify([r.fromSchema,r.fromTable,r.fromColumn,r.toSchema,r.toTable,r.toColumn]);
    for(const r of schema.relations)seen.add(signature(r));
    const same=(a,b)=>String(a).toLowerCase()===String(b).toLowerCase();
    for(const table of schema.tables)for(const column of table.columns){
        const comment=column.comment||'';
        const references=[...comment.matchAll(/(?:→|->|关联|引用|外键|对应)[^A-Za-z0-9_]{0,32}([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*){0,2})/g)];
        if(/自关联|自引用/.test(comment))references.push(['自关联',`${table.schema}.${table.name}`]);
        for(const match of references){
            const parts=match[1].split('.');
            let candidates=[],field;
            if(parts.length===3){candidates=schema.tables.filter(t=>same(t.schema,parts[0])&&same(t.name,parts[1]));field=parts[2];}
            else if(parts.length===2){
                candidates=schema.tables.filter(t=>same(t.schema,parts[0])&&same(t.name,parts[1]));
                if(!candidates.length){candidates=schema.tables.filter(t=>same(t.name,parts[0]));field=parts[1];}
            }else candidates=schema.tables.filter(t=>same(t.name,parts[0]));
            if(candidates.length>1){const local=candidates.filter(t=>same(t.schema,table.schema));if(local.length===1)candidates=local;}
            if(!candidates.length&&!/^(?:→|->)/.test(match[0])&&!/[_.]/.test(match[1]))continue;
            if(candidates.length!==1){unresolved.push(`${table.schema}.${table.name}.${column.name} → ${match[1]}（目标表不存在或不唯一）`);continue;}
            const target=candidates[0],pk=target.columns.filter(c=>c.primaryKey);
            const targetColumn=field?target.columns.find(c=>same(c.name,field)):pk.length===1?pk[0]:pk.length===0?target.columns.find(c=>same(c.name,'id')):null;
            if(!targetColumn){unresolved.push(`${table.schema}.${table.name}.${column.name} → ${match[1]}（目标字段不存在或未明确指定）`);continue;}
            const relation={fromSchema:table.schema,fromTable:table.name,fromColumn:column.name,toSchema:target.schema,toTable:target.name,toColumn:targetColumn.name,inferred:true,evidence:comment};
            const id=signature(relation);if(seen.has(id))continue;seen.add(id);relations.push(relation);
        }
    }
    // Resolve conventional *_id references within the same module and schema.
    // Explicit comment references take precedence; ambiguous candidates are skipped.
    for(const table of schema.tables)for(const column of table.columns){
        if(!/_id$/i.test(column.name)||/(?:→|->)/.test(column.comment||''))continue;
        if([...schema.relations,...relations].some(r=>same(r.fromSchema,table.schema)&&same(r.fromTable,table.name)&&same(r.fromColumn,column.name)))continue;
        const role=column.name.slice(0,-3),parts=table.name.split('_');
        let target=null;
        for(let length=parts.length;length>=1;length--){
            const candidateName=parts.slice(0,length).join('_')+'_'+role;
            const candidates=schema.tables.filter(t=>same(t.schema,table.schema)&&same(t.name,candidateName)&&t!==table);
            if(candidates.length===1){target=candidates[0];break;}
        }
        if(!target)continue;
        const pk=target.columns.filter(c=>c.primaryKey);
        const field=pk.length===1?pk[0]:pk.length===0?target.columns.find(c=>same(c.name,'id')):null;
        if(!field||!same(column.type,field.type))continue;
        const relation={fromSchema:table.schema,fromTable:table.name,fromColumn:column.name,toSchema:target.schema,toTable:target.name,toColumn:field.name,inferred:true,evidence:`字段命名推断：${table.name}.${column.name} 对应同模块 ${target.name}.${field.name}；字段注释：${column.comment||'无'}`};
        const id=signature(relation);if(seen.has(id))continue;seen.add(id);relations.push(relation);
    }
    return {relations,unresolved};
}
