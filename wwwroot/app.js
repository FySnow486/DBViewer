'use strict';
const $ = id => document.getElementById(id);
const esc = v => String(v ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
let database, selected, current = null, provider = 'sqlserver', mode = 'table', zoom = 1, positions = {}, busy = false;
const connections = [];
const offlineSnapshots = new Map();
const workspaces = new Map();
function rememberWorkspace(){if(database&&(!current||current.id))workspaces.set(current?.id??"demo",{database,current,selected,positions,zoom,mode,tableSearch:$("tableSearch").value,fieldSearch:$("fieldSearch").value});}
function restoreWorkspace(state){applySchema(state.database,state.current);selected=state.selected;positions=state.positions;zoom=state.zoom;$("tableSearch").value=state.tableSearch;$("fieldSearch").value=state.fieldSearch;renderList();renderTable();switchMode(state.mode);}
function renderConnectionList(){
    $("connectionList").innerHTML="";
    for(const c of [...connections,...offlineSnapshots.values()]){
        const row=document.createElement("div");row.className="workspace-connection"+(current?.id===c.id?" active":"");
        const button=document.createElement("button");button.type="button";button.className="switch-connection";button.innerHTML=`<span>▤</span><span><strong>${esc(c.name)}</strong><small>${esc(providerNames[c.provider]??c.provider)}${c.offline?" · 离线快照":""}${workspaces.has(c.id)?" · 已加载":""}</small></span>`;button.title=c.name;button.disabled=busy;button.onclick=()=>switchConnection(c.id);
        const edit=document.createElement("button");edit.type="button";edit.className="edit-connection";edit.textContent="⚙";edit.setAttribute("aria-label",`编辑连接 ${c.name}`);edit.onclick=async()=>{await openConnection();try{await chooseProfile(c.id);}catch(e){toast(e.message);}};
        if(c.offline){edit.textContent='×';edit.setAttribute('aria-label',`移除快照 ${c.name}`);edit.onclick=async()=>{try{await snapshotStore.remove(c.id);offlineSnapshots.delete(c.id);workspaces.delete(c.id);if(localStorage.getItem('dbviewer-active-snapshot')===c.id)localStorage.removeItem('dbviewer-active-snapshot');renderConnectionList();toast('已移除本机快照，导出文件仍可再次导入');}catch(e){toast(e.message);}};}
        row.append(button,edit);$("connectionList").appendChild(row);
    }
    if(!connections.length&&!offlineSnapshots.size)$("connectionList").innerHTML='<p class="connection-empty">添加连接后，可在这里切换数据库</p>';
}
async function switchConnection(id){
    if(busy||current?.id===id)return;
    rememberWorkspace();busy=true;renderConnectionList();$("connectionStatus").textContent="正在切换连接…";
    try{
        let state=workspaces.get(id);
        if(!state&&offlineSnapshots.has(id)){const c=offlineSnapshots.get(id);state={database:c.schema,current:c,selected:c.schema.tables[0]?key(c.schema.tables[0]):null,positions:{},zoom:1,mode,tableSearch:'',fieldSearch:''};}
        if(!state){const c=await request("/api/connections/"+id);const data=await request("/api/schema",c);state={database:data,current:c,selected:data.tables[0]?key(data.tables[0]):null,positions:{},zoom:1,mode,tableSearch:"",fieldSearch:""};}
        if(state.current.offline)localStorage.setItem('dbviewer-active-snapshot',id);
        else{await request("/api/connections/"+id+"/activate",null,"POST");localStorage.removeItem('dbviewer-active-snapshot');}
        restoreWorkspace(state);rememberWorkspace();toast("已切换到 "+state.current.name);
    }catch(e){$("connectionStatus").textContent="切换失败，保留当前数据库";toast(e.message);}
    finally{busy=false;renderConnectionList();}
}
const connectionDrafts = new Map();
let businessRelations=[],unresolvedRelations=[];
function graphRelations(){return $('showBusinessRelations').checked?[...database.relations,...businessRelations]:database.relations;}
function updateRelationSummary(){
    const visible=new Set(filteredTables().map(key));
    const shown=graphRelations().filter(r=>visible.has(`${r.fromSchema}.${r.fromTable}`)&&visible.has(`${r.toSchema}.${r.toTable}`));
    const inferred=shown.filter(r=>r.inferred).length;
    $('erRelationSummary').textContent=`当前：${shown.length-inferred} 条外键 · ${inferred} 条业务关联${unresolvedRelations.length?' · '+unresolvedRelations.length+' 处注释引用未解析':''}`;
    $('erRelationSummary').title=unresolvedRelations.join('\n');
}
const providerNames = {postgresql:'PostgreSQL',mysql:'MySQL',sqlserver:'SQL Server',sqlite:'SQLite'};
const examples = {postgresql:'Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=your_password;Timeout=10',mysql:'Server=localhost;Port=3306;Database=mydb;User ID=root;Password=your_password;Connection Timeout=10',sqlserver:'Server=localhost,1433;Database=mydb;User ID=sa;Password=your_password;Encrypt=True;Connect Timeout=10',sqlite:'Data Source=/absolute/path/database.db'};
const key = t => `${t.schema}.${t.name}`;
const isFrom = (r,t) => r.fromSchema===t.schema && r.fromTable===t.name;
const isTo = (r,t) => r.toSchema===t.schema && r.toTable===t.name;
const related = t => graphRelations().filter(r=>isFrom(r,t)||isTo(r,t));
function toast(message){$('toast').textContent=message;$('toast').classList.add('visible');clearTimeout(toast.timer);toast.timer=setTimeout(()=>$('toast').classList.remove('visible'),3200);}
async function request(url,body,method){const res=await fetch(url,method?{method,headers:{'Content-Type':'application/json'},body:body?JSON.stringify(body):undefined}:body?{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)}:{});if(res.status===204)return null;const data=await res.json();if(!res.ok)throw Error(data.error||data.detail||'请求失败，请稍后重试。');return data;}
function applySchema(data,connection){database=data;({relations:businessRelations,unresolved:unresolvedRelations}=inferBusinessRelations(data));if(data.businessRelations){const links=new Map([...businessRelations,...data.businessRelations].map(r=>[JSON.stringify([r.fromSchema,r.fromTable,r.fromColumn,r.toSchema,r.toTable,r.toColumn]),r]));businessRelations=[...links.values()];}current=connection;$('refreshButton').disabled=!!connection?.offline;$('refreshButton').title=connection?.offline?'离线快照不连接数据库，重新导入文件可更新结构':'';selected=data.tables.find(t=>key(t)===selected)?selected:(data.tables[0]?key(data.tables[0]):null);positions={};$('databaseName').textContent=data.name||'未命名数据库';$('connectionName').textContent=connection?.name||'电商示例数据库';$('connectionMeta').textContent=`${providerNames[data.provider]} · ${connection?.offline?'离线快照':connection?'已连接':'演示模式'}`;$('demoBadge').hidden=!!connection;$('connectionStatus').textContent=connection?.offline?'离线查看 · 不连接数据库':connection?'结构读取成功':'演示数据已就绪';$('statTables').innerHTML=`${data.tables.length} <small>张</small>`;$('statColumns').innerHTML=`${data.tables.reduce((n,t)=>n+t.columns.length,0)} <small>个</small>`;$('statRelations').innerHTML=`${data.relations.length} <small>组</small>`;$('tableCount').textContent=data.tables.length;const schemas=[...new Set(data.tables.map(t=>t.schema))];$('schemaLabel').textContent=schemas.length===1?schemas[0]:`${schemas.length} 个 Schema`;$('tableSearch').value='';$('fieldSearch').value='';renderList();renderTable();if(mode==='er')renderER();renderConnectionList();}
function filteredTables(){
    const words=$('tableSearch').value.trim().toLowerCase().split(/\s+/).filter(Boolean);
    return database.tables.filter(t=>{
        const text=[t.schema,t.name,t.comment,...t.columns.flatMap(c=>[c.name,c.type,c.length,c.comment])].join(' ').toLowerCase();
        return words.every(word=>text.includes(word));
    });
}
function filterStructure(value){
    if(!database)return;
    $('tableSearch').value=value;$('structureFilter').value=value;
    const tables=filteredTables();
    if(!tables.some(t=>key(t)===selected)){selected=tables[0]?key(tables[0]):null;$('fieldSearch').value='';}
    renderList();renderTable();if(mode==='er')renderER();
}
function renderList(){renderAllTables();const tables=filteredTables();$('structureFilter').value=$('tableSearch').value;$('filterCount').textContent=`${tables.length} / ${database.tables.length} 张表`;$('clearFilter').disabled=!$('tableSearch').value;$('tableCount').textContent=tables.length;$('tableList').innerHTML='';tables.forEach(t=>{const b=document.createElement('button');b.className=key(t)===selected?'active':'';b.innerHTML=`<span class="table-nav-icon">▦</span><span>${esc(t.name)}</span><small>${t.columns.length}</small>`;b.title=key(t);b.onclick=()=>{selected=key(t);$('fieldSearch').value='';renderList();renderTable();if(mode==='all')switchMode('table');else if(mode==='er'){highlightER();focusERTable('smooth');}};$('tableList').appendChild(b);});if(!$('tableList').children.length)$('tableList').innerHTML='<div class="empty-state">没有匹配的数据表</div>';}
function renderTable(){const t=database.tables.find(t=>key(t)===selected);if(!t){$('selectedTable').textContent=$('tableSearch').value.trim()?'没有匹配的数据表':'暂无数据表';$('selectedComment').textContent=$('tableSearch').value.trim()?'请更换关键字，或点击清除筛选。':'此数据库为空，或当前账号没有表结构读取权限。';$('selectedSchema').textContent='';$('columnRows').innerHTML='<tr><td colspan="7" class="empty-state">没有可显示的字段</td></tr>';$('fieldCount').textContent='0 个字段';$('selectedRelationCount').textContent='0';$('relationList').innerHTML='';return;}
$('selectedTable').textContent=t.name;$('selectedComment').textContent=t.comment||'暂无表注释';$('selectedSchema').textContent=t.schema;const query=$('fieldSearch').value.toLowerCase();const fields=t.columns.filter(c=>`${c.name} ${c.type} ${c.comment}`.toLowerCase().includes(query));$('columnRows').innerHTML=fields.map(c=>{const fk=database.relations.some(r=>isFrom(r,t)&&r.fromColumn===c.name);const symbol=c.primaryKey?'⚿':fk?'⑂':'·';const style=/int|numeric|decimal|float|double|real/.test(c.type)?'numeric':/time|date/.test(c.type)?'time':'';return `<tr><td>${String(t.columns.indexOf(c)+1).padStart(2,'0')}</td><td class="column-name"><span title="${c.primaryKey?'主键':fk?'外键':'字段'}" class="column-symbol ${c.primaryKey?'pk':fk?'fk':''}">${symbol}</span>${esc(c.name)}${c.primaryKey&&fk?' <span title="外键" class="foreign-legend">⑂</span>':''}</td><td><span class="type-chip ${style}">${esc(c.type||'未声明')}</span></td><td>${esc(c.length||'—')}</td><td><span class="nullable ${c.nullable?'yes':''}">${c.nullable?'YES':'NO'}</span></td><td class="default-value">${esc(c.defaultValue||'—')}</td><td>${esc(c.comment||'—')}</td></tr>`;}).join('')||'<tr><td colspan="7" class="empty-state">没有匹配的字段</td></tr>';$('fieldCount').textContent=`共 ${t.columns.length} 个字段${query?` · 匹配 ${fields.length} 个`:''}`;const rel=related(t);$('selectedRelationCount').textContent=rel.length;$('relationList').innerHTML=rel.length?rel.map(r=>`<span class="relation-chip ${r.inferred?'business-relation':''}" title="${esc(r.inferred?'业务关联推断依据：'+r.evidence:'数据库外键')}"><b>${esc(r.fromTable)}.${esc(r.fromColumn)}</b><span class="arrow">→</span>${esc(r.toTable)}.${esc(r.toColumn)}${r.inferred?' <small>业务关联</small>':''}</span>`).join(''):'<span style="color:#a4a9b7;font-size:10px">未定义外键约束</span>';}
function renderAllTables(){
    const tables=filteredTables();
    $('allTableRows').innerHTML=tables.map((t,i)=>`<tr><td>${i+1}</td><td>${esc(t.schema)}</td><td><button class="table-detail-link" type="button" data-table-index="${i}">${esc(t.name)}</button></td><td>${t.columns.length}</td><td class="table-overview-comment">${esc(t.comment||'暂无表注释')}</td></tr>`).join('')||'<tr><td colspan="5" class="empty-state">没有匹配的数据表，请更换关键字或清除筛选。</td></tr>';
    $('allTableRows').querySelectorAll('[data-table-index]').forEach(button=>{
        button.onclick=()=>{selected=key(tables[Number(button.dataset.tableIndex)]);$('fieldSearch').value='';renderList();renderTable();switchMode('table');};
    });
    $('allTableCount').textContent=`显示 ${tables.length} 张表 · 数据库共 ${database.tables.length} 张表`;
}
function switchMode(next){
    mode=next;document.body.classList.toggle('er-mode',next==='er');
    for(const m of ['all','table','er']){
        $(`${m}View`).hidden=m!==next;
        $(`${m}Tab`).classList.toggle('active',m===next);
        $(`${m}Tab`).setAttribute('aria-selected',String(m===next));
    }
    $('toolbarNote').textContent=next==='all'?'表名称与注释':next==='table'?'字段定义与约束':'基于数据库外键生成';
    if(next==='all')renderAllTables();else if(next==='er')renderER();
}
const ER_CARD_WIDTH = 560;
const erNodes = new Map();
let erPadding = {x:400,y:300};

function layoutER(){
    const tables = filteredTables(), neighbors = new Map(tables.map(t=>[key(t),new Set()]));
    for(const r of graphRelations()){
        const a=`${r.fromSchema}.${r.fromTable}`, b=`${r.toSchema}.${r.toTable}`;
        if(a!==b && neighbors.has(a) && neighbors.has(b)){neighbors.get(a).add(b);neighbors.get(b).add(a);}
    }
    const remaining = new Set(neighbors.keys());
    const components=[];
    while(remaining.size){
        // Connected groups stay together; highly connected tables anchor each group.
        const root=[...remaining].sort((a,b)=>neighbors.get(b).size-neighbors.get(a).size||a.localeCompare(b))[0];
        const layers=[[root]]; remaining.delete(root);
        for(let level=0;level<layers.length;level++){
            const next=[];
            for(const id of layers[level]) for(const neighbor of [...neighbors.get(id)].sort()){
                if(remaining.delete(neighbor))next.push(neighbor);
            }
            if(next.length)layers.push(next);
        }
        // Keep each relationship layer at most three cards high, spilling right.
        const columns=layers.flatMap(layer=>{
            const chunks=[];
            for(let i=0;i<layer.length;i+=3)chunks.push(layer.slice(i,i+3));
            return chunks;
        });
        const heights=columns.map(column=>column.reduce((sum,id)=>sum+erNodes.get(id).offsetHeight,0)+(column.length-1)*70);
        const groupHeight=Math.max(...heights);
        columns.forEach((column,index)=>{
            let y=(groupHeight-heights[index])/2;
            column.forEach(id=>{positions[id]={x:index*(ER_CARD_WIDTH+180),y};y+=erNodes.get(id).offsetHeight+70;});
        });
        components.push({ids:columns.flat(),width:columns.length*(ER_CARD_WIDTH+180)-180,height:groupHeight});
    }
    // Bound vertical growth; new groups continue in columns to the right.
    const targetHeight=Math.max(1400,$('erViewport').clientHeight/zoom);
    let x=erPadding.x,y=erPadding.y,columnWidth=0;
    for(const group of components){
        if(y>erPadding.y&&y+group.height>erPadding.y+targetHeight){
            x+=columnWidth+220;y=erPadding.y;columnWidth=0;
        }
        for(const id of group.ids){positions[id].x+=x;positions[id].y+=y;}
        y+=group.height+140;columnWidth=Math.max(columnWidth,group.width);
    }
}
function focusERTable(behavior='auto'){
    const node=erNodes.get(selected),pos=positions[selected],viewport=$('erViewport');
    if(!node||!pos)return;
    viewport.scrollTo({left:(pos.x+ER_CARD_WIDTH/2)*zoom-viewport.clientWidth/2,
        top:(pos.y+node.offsetHeight/2)*zoom-viewport.clientHeight/2,behavior});
}
function highlightER(){
    for(const [id,node] of erNodes)node.classList.toggle('selected',id===selected);
    drawLines();
}
function renderER(){
    const viewport=$('erViewport');
    erPadding={x:Math.max(400,viewport.clientWidth/(2*.4)),y:Math.max(300,viewport.clientHeight/(2*.4))};
    $('erNodes').innerHTML='';erNodes.clear();
    const tables=filteredTables();$('erEmpty').hidden=tables.length>0;
    for(const t of tables){
        const id=key(t),node=document.createElement('article');
        node.className='er-node';node.dataset.table=id;node.setAttribute('aria-label',id);
        node.innerHTML=`<h3><span>▦ ${esc(t.name)}</span><small>${esc(t.schema)} · ${t.columns.length} 字段</small></h3>
            <div class="er-table-comment" title="${esc(t.comment)}"><span>${esc(t.comment||'暂无表注释')}</span></div>
            <div class="er-column-labels"><span>字段名称</span><span>类型 / 长度</span><span>注释</span></div>`+
            t.columns.map((c,i)=>{
                const fk=database.relations.some(r=>isFrom(r,t)&&r.fromColumn===c.name);
                const type=c.type+(c.length?`(${c.length})`:'');
                return `<div class="er-column" data-column-index="${i}"><span class="er-field-name" title="${esc(c.name)}"><span class="${c.primaryKey?'pk':fk?'fk':''}" title="${c.primaryKey?'主键':fk?'外键':'字段'}">${c.primaryKey?'⚿':fk?'⑂':'·'}</span>${esc(c.name)}</span><span class="col-type" title="${esc(type)}">${esc(type||'未声明')}</span><span class="er-field-comment" title="${esc(c.comment)}">${esc(c.comment||'—')}</span></div>`;
            }).join('');
        node.onclick=()=>{selected=id;renderList();renderTable();highlightER();};
        node.ondblclick=()=>{selected=id;$('fieldSearch').value='';renderList();renderTable();switchMode('table');};
        const handle=node.querySelector('h3');
        handle.onpointerdown=e=>{
            if(e.button!==0)return;
            const startX=e.clientX,startY=e.clientY,start={...positions[id]};
            handle.setPointerCapture(e.pointerId);handle.style.cursor='grabbing';
            handle.onpointermove=ev=>{
                const pos=positions[id];pos.x=Math.max(erPadding.x/2,start.x+(ev.clientX-startX)/zoom);pos.y=Math.max(erPadding.y/2,start.y+(ev.clientY-startY)/zoom);
                node.style.left=pos.x+'px';node.style.top=pos.y+'px';drawLines();
            };
            handle.onpointerup=handle.onlostpointercapture=()=>{handle.onpointermove=null;handle.style.cursor='grab';};
        };
        $('erNodes').appendChild(node);erNodes.set(id,node);
    }
    if(tables.some(t=>!positions[key(t)]))layoutER();
    for(const [id,node] of erNodes){node.style.left=positions[id].x+'px';node.style.top=positions[id].y+'px';}
    highlightER();setZoom(zoom,false);focusERTable();
}
function drawLines(){
    updateRelationSummary();
    if(!erNodes.size){$('erLines').innerHTML='';$('erWorld').style.width='100%';$('erWorld').style.height='100%';$('erCanvas').style.width='0px';$('erCanvas').style.height='0px';return;}
    const width=Math.max(1100,...[...erNodes].map(([id])=>positions[id].x+ER_CARD_WIDTH+erPadding.x));
    const height=Math.max(650,...[...erNodes].map(([id,node])=>positions[id].y+node.offsetHeight+erPadding.y));
    $('erCanvas').style.width=width+'px';$('erCanvas').style.height=height+'px';
    $('erWorld').style.width=width*zoom+'px';$('erWorld').style.height=height*zoom+'px';
    $('erLines').setAttribute('viewBox',`0 0 ${width} ${height}`);
    const columnY=(node,t,column)=>{
        const index=t.columns.findIndex(c=>c.name===column),row=node.querySelector(`[data-column-index="${index}"]`);
        return row?row.offsetTop+row.offsetHeight/2:24;
    };
    $('erLines').innerHTML='<defs><marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M1 1 L9 5 L1 9" fill="none" stroke="#a795d0" stroke-width="1.5"/></marker><marker id="activeArrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M1 1 L9 5 L1 9" fill="none" stroke="#7560cc" stroke-width="1.5"/></marker></defs>'+
        graphRelations().map((r,i)=>{
            const from=database.tables.find(t=>isFrom(r,t)),to=database.tables.find(t=>isTo(r,t));
            if(!from||!to||!erNodes.has(key(from))||!erNodes.has(key(to)))return '';
            const a=positions[key(from)],b=positions[key(to)];
            const y1=a.y+columnY(erNodes.get(key(from)),from,r.fromColumn),y2=b.y+columnY(erNodes.get(key(to)),to,r.toColumn);
            const sameColumn=Math.abs(a.x-b.x)<ER_CARD_WIDTH,self=key(from)===key(to),right=a.x<b.x;
            const x1=a.x+(sameColumn||right?ER_CARD_WIDTH:0),x2=b.x+(sameColumn||!right?ER_CARD_WIDTH:0);
            let d;
            if(sameColumn){const lane=Math.max(x1,x2)+40+(i%5)*16;d=`M${x1} ${y1} C${lane} ${y1},${lane} ${y2},${x2} ${y2}`;}
            else {const lane=(x1+x2)/2+(i%3-1)*16;d=`M${x1} ${y1} C${lane} ${y1},${lane} ${y2},${x2} ${y2}`;}
            const active=key(from)===selected||key(to)===selected;
            return `<path class="er-link ${active?'active':''}" d="${d}" fill="none" stroke="${active?'#7560cc':'#c5bfd9'}" stroke-width="${active?1.8:1.1}" ${r.inferred?'stroke-dasharray="7 5"':''} marker-end="url(#${active?'activeArrow':'arrow'})"><title>${esc(key(from)+'.'+r.fromColumn+' → '+key(to)+'.'+r.toColumn)}${self?'（自关联）':''}${r.inferred?' · 业务关联推断依据：'+esc(r.evidence):' · 数据库外键'}</title></path>`;
        }).join('');
}
function setZoom(value,keepCenter=true,anchor=null){
    const viewport=$('erViewport'),oldZoom=zoom;
    const point=anchor??{x:viewport.clientWidth/2,y:viewport.clientHeight/2};
    const center={x:(viewport.scrollLeft+point.x)/oldZoom,y:(viewport.scrollTop+point.y)/oldZoom};
    zoom=Math.min(1.5,Math.max(.4,value));$('erCanvas').style.transform=`scale(${zoom})`;$('zoomLabel').textContent=Math.round(zoom*100)+'%';
    drawLines();
    if(keepCenter)viewport.scrollTo(center.x*zoom-point.x,center.y*zoom-point.y);
}

// Middle-button panning works over both empty canvas and table cards.
const panViewport=$('erViewport');
panViewport.addEventListener('wheel',e=>{
    if(!e.ctrlKey)return;
    e.preventDefault();
    if(canvasPan)finishCanvasPan();
    const bounds=panViewport.getBoundingClientRect();
    const delta=e.deltaY*(e.deltaMode===1?16:e.deltaMode===2?panViewport.clientHeight:1);
    setZoom(zoom*Math.exp(-Math.max(-300,Math.min(300,delta))*.002),true,
        {x:e.clientX-bounds.left-panViewport.clientLeft,y:e.clientY-bounds.top-panViewport.clientTop});
},{passive:false});
let canvasPan=null;
panViewport.addEventListener('pointerdown',e=>{
    if(e.button!==1)return;
    e.preventDefault();e.stopPropagation();
    canvasPan={pointerId:e.pointerId,x:e.clientX,y:e.clientY,left:panViewport.scrollLeft,top:panViewport.scrollTop};
    panViewport.setPointerCapture(e.pointerId);panViewport.classList.add('panning');
},{capture:true});
panViewport.addEventListener('pointermove',e=>{
    if(!canvasPan||e.pointerId!==canvasPan.pointerId)return;
    e.preventDefault();
    panViewport.scrollLeft=canvasPan.left-(e.clientX-canvasPan.x);
    panViewport.scrollTop=canvasPan.top-(e.clientY-canvasPan.y);
});
function finishCanvasPan(e){
    if(!canvasPan||(e?.pointerId!=null&&e.pointerId!==canvasPan.pointerId))return;
    const id=canvasPan.pointerId;canvasPan=null;panViewport.classList.remove('panning');
    if(panViewport.hasPointerCapture(id))panViewport.releasePointerCapture(id);
}
for(const event of ['pointerup','pointercancel','lostpointercapture'])panViewport.addEventListener(event,finishCanvasPan);
window.addEventListener('blur',()=>finishCanvasPan());
panViewport.addEventListener('auxclick',e=>{if(e.button===1)e.preventDefault();});
window.addEventListener('resize',()=>{if(mode==='er'){drawLines();focusERTable();}});

function selectProvider(value){
    // Keep each provider's edits in page memory when switching database types.
    if ($('connectionString').value) connectionDrafts.set(provider,$('connectionString').value);
    provider=value;
    document.querySelectorAll('[data-provider]').forEach(b=>b.classList.toggle('selected',b.dataset.provider===provider));
    $('connectionString').value=connectionDrafts.get(provider)??examples[provider];
    $('connectionString').placeholder='粘贴连接字符串，或直接修改上面的模板';
    $('connectionHint').textContent=provider==='sqlite'?'直接修改文件路径；只读打开已有文件，不会自动创建数据库。':provider==='sqlserver'?'将 mydb 和 your_password 替换为实际数据库名与密码。本机容器若提示证书验证失败，可添加 TrustServerCertificate=True;（仅用于你信任的本机实例）。':'直接修改服务器、数据库、用户名和密码；也可以粘贴完整连接字符串。';
}
let editingConnectionId=null;
async function refreshProfiles(){const state=await request('/api/connections');connections.splice(0,connections.length,...state.items);renderConnectionList();return state;}
async function chooseProfile(id){const c=await request('/api/connections/'+id);selectProvider(c.provider);editingConnectionId=c.id;$('configName').value=c.name;$('connectionString').value=c.connectionString;}
function renderProfiles(){
    $('savedConnections').innerHTML='';
    for(const c of connections){
        const row=document.createElement('span');row.className='saved-profile';
        const select=document.createElement('button');select.type='button';select.textContent=`${c.name} · ${providerNames[c.provider]}`;
        select.onclick=()=>chooseProfile(c.id).catch(e=>toast(e.message));
        const remove=document.createElement('button');remove.type='button';remove.className='delete-profile';remove.textContent='×';remove.setAttribute('aria-label',`删除连接 ${c.name}`);
        remove.onclick=async()=>{try{await request('/api/connections/'+c.id,null,'DELETE');if(editingConnectionId===c.id)editingConnectionId=null;workspaces.delete(c.id);if(current?.id===c.id){current.id=null;}await refreshProfiles();renderProfiles();toast('已删除保存的连接');}catch(e){toast(e.message);}};
        row.append(select,remove);$('savedConnections').appendChild(row);
    }
}
async function openConnection(editCurrent=false){
    if(current?.offline)editCurrent=false;
    selectProvider(editCurrent&&current?current.provider:provider);editingConnectionId=editCurrent?current?.id??null:null;
    if(editCurrent&&current){$('configName').value=current.name;$('connectionString').value=current.connectionString;}else $('configName').value='';
    $('connectionError').hidden=true;$('connectionDialog').showModal();
    try{await refreshProfiles();renderProfiles();}catch(e){toast(e.message);}
}
async function initialize(){
    try{
        for(const item of await snapshotStore.list())offlineSnapshots.set(item.id,item);
        const active=localStorage.getItem('dbviewer-active-snapshot');
        if(active&&offlineSnapshots.has(active)){await switchConnection(active);refreshProfiles().catch(()=>{});return;}
    }catch(e){toast(e.message);}
    try{
        const state=await refreshProfiles();
        if(state.activeId){const c=await request('/api/connections/'+state.activeId);applySchema(await request('/api/schema',c),c);rememberWorkspace();renderConnectionList();return;}
    }catch(e){toast('自动恢复连接失败：'+e.message);}
    await loadDemo();
}
async function loadDemo(){if(busy)return;rememberWorkspace();busy=true;try{const cached=workspaces.get('demo');if(cached)restoreWorkspace(cached);else applySchema(await request('/api/demo'),null);localStorage.removeItem('dbviewer-active-snapshot');if($('connectionDialog').open)$('connectionDialog').close();}catch(e){toast(e.message);}finally{busy=false;renderConnectionList();}}
$('connectionForm').onsubmit=async e=>{
    e.preventDefault();if(busy)return;rememberWorkspace();busy=true;
    const connection={id:editingConnectionId,name:$('configName').value.trim(),provider,connectionString:$('connectionString').value.trim()};
    $('connectSubmit').disabled=true;$('connectSubmit').textContent='正在连接并保存…';$('connectionError').hidden=true;
    try{
        const data=await request('/api/schema',connection);
        const saved=await request('/api/connections',connection);connection.id=saved.id;
        workspaces.delete(connection.id);zoom=1;selected=null;applySchema(data,connection);localStorage.removeItem('dbviewer-active-snapshot');rememberWorkspace();await refreshProfiles();connectionDrafts.set(provider,connection.connectionString);
        $('connectionDialog').close();toast('连接成功，已加密保存到本机');
    }catch(e){$('connectionError').textContent=e.message;$('connectionError').hidden=false;}
    finally{busy=false;renderConnectionList();$('connectSubmit').disabled=false;$('connectSubmit').textContent='连接并保存 →';}
};
$('newConnection').onclick=()=>openConnection();
for(const id of ['connectionButton','manageConnections'])$(id).onclick=()=>openConnection(true);
$('closeDialog').onclick=()=>$('connectionDialog').close();document.querySelectorAll('[data-provider]').forEach(b=>b.onclick=()=>selectProvider(b.dataset.provider));$('demoButton').onclick=loadDemo;$('tableSearch').oninput=e=>filterStructure(e.target.value);$('structureFilter').oninput=e=>filterStructure(e.target.value);$('clearFilter').onclick=()=>filterStructure('');$('fieldSearch').oninput=()=>database&&renderTable();$('showBusinessRelations').onchange=()=>{if(!database)return;positions={};renderTable();if(mode==='er')renderER();};$('allTab').onclick=()=>database&&switchMode('all');$('tableTab').onclick=()=>database&&switchMode('table');$('erTab').onclick=()=>database&&switchMode('er');$('zoomIn').onclick=()=>setZoom(zoom+.1);$('zoomOut').onclick=()=>setZoom(zoom-.1);$('focusTable').onclick=()=>focusERTable('smooth');$('resetLayout').onclick=()=>{positions={};zoom=1;renderER();};$('helpButton').onclick=()=>$('helpDialog').showModal();$('closeHelp').onclick=()=>$('helpDialog').close();
$('refreshButton').onclick=async()=>{if(busy||current?.offline)return;busy=true;$('refreshButton').disabled=true;try{applySchema(await request(current?'/api/schema':'/api/demo',current),current);toast('结构已刷新');}catch(e){toast(e.message);}finally{busy=false;$('refreshButton').disabled=false;}};
$('exportButton').onclick=()=>{
    if(!database)return;
    try{
        const snapshot=createSnapshot(database,businessRelations);
        const url=URL.createObjectURL(new Blob([JSON.stringify(snapshot,null,2)],{type:'application/json'}));
        const a=document.createElement('a');a.href=url;a.download=`${(database.name||'database').replace(/[\\/:*?"<>|]/g,'_')}.dbviewer.json`;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
        toast('结构快照已导出，包含全部表及关联，不含连接密码');
    }catch(e){toast(e.message);}
};
$('exportExcelButton').onclick=async()=>{
    if(!database||busy)return;
    const button=$('exportExcelButton');button.disabled=true;
    try{
        const response=await fetch('/api/export/excel',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(database)});
        if(!response.ok){const error=await response.json();throw Error(error.error||'Excel 导出失败。');}
        const url=URL.createObjectURL(await response.blob());
        const a=document.createElement('a');a.href=url;a.download=`${(database.name||'database').replace(/[\\/:*?"<>|]/g,'_')}.xlsx`;a.click();setTimeout(()=>URL.revokeObjectURL(url),60000);
        toast(`已导出 ${database.tables.length} 张表的字段信息`);
    }catch(e){toast(e.message);}finally{button.disabled=false;}
};
$('importButton').onclick=()=>$('snapshotFile').click();
$('snapshotFile').onchange=async e=>{
    const file=e.target.files[0];if(!file)return;
    try{
        if(busy)throw Error('正在读取数据库，请稍后导入。');
        if(file.size>20*1024*1024)throw Error('快照文件不能超过 20 MB。');
        const schema=normalizeSnapshot(JSON.parse((await file.text()).replace(/^\uFEFF/,'')));
        const item={id:'offline:'+Array.from(crypto.getRandomValues(new Uint8Array(16)),b=>b.toString(16).padStart(2,'0')).join(''),name:schema.name+' · 快照',provider:schema.provider,offline:true,schema};
        await snapshotStore.save(item);offlineSnapshots.set(item.id,item);await switchConnection(item.id);toast('已导入并保存到本机，可直接离线查看');
    }catch(e){toast(e instanceof SyntaxError?'文件不是有效的 JSON 结构快照。':e.message);}
    finally{e.target.value='';}
};
let checkingService=false;
async function checkService(){
    if(checkingService)return;checkingService=true;
    try{const response=await fetch('/api/health',{cache:'no-store',signal:AbortSignal.timeout(4000)});if(!response.ok)throw Error();const status=await response.json();if(status.status!=='online')throw Error();$('serviceStatus').textContent='● 服务在线';$('serviceStatus').classList.remove('offline');$('serviceDetail').textContent='本地服务在线。在线状态仅表示 DBViewer 正在运行，不代表数据库已连接。';}
    catch{$('serviceStatus').textContent='● 服务离线';$('serviceStatus').classList.add('offline');$('serviceDetail').textContent='无法连接 DBViewer 服务，请启动程序。已加载的快照仍可继续浏览。';}
    finally{checkingService=false;}
}
$('serviceStatus').onclick=()=>{$('serviceAddress').textContent=location.origin;checkService();$('serviceDialog').showModal();};
$('closeServiceDialog').onclick=()=>$('serviceDialog').close();
checkService();setInterval(checkService,15000);

document.addEventListener('keydown',e=>{if((e.key==='/'||(e.key.toLowerCase()==='k'&&(e.metaKey||e.ctrlKey)))&&!['INPUT','TEXTAREA'].includes(document.activeElement.tagName)&&!$('connectionDialog').open&&!$('helpDialog').open){e.preventDefault();$('tableSearch').focus();}});
initialize();
