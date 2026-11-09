/* Deliberately local, ephemeral scenario data. No backend or actual operations. */
const $ = s => document.querySelector(s);
const states = {
  uninstalled:{name:'未安装',kicker:'↓ 准备安装',title:'准备好前往基沃托斯了吗？',description:'选择安装位置，或直接使用已经安装的游戏。',meta:['安装位置','D:\\Games\\BlueArchive'],primary:'安装游戏',action:'install',secondary:[['更改路径','path'],['选择已有目录','existing']],rule:'路径和选择已有目录入口持续可见。'},
  checking:{name:'检查中',kicker:'◌ 正在检查',title:'正在检查游戏文件',description:'正在读取安装状态与版本信息。',meta:['完成后显示可执行的操作'],primary:'正在检查…',disabled:true,rule:'不展示虚假的百分比；避免重复发起操作。'},
  update:{name:'有更新',kicker:'↓ 更新可用',title:'有新的游戏文件可用',description:'更新完成后即可开始游戏。',meta:['当前安装位置','D:\\Games\\BlueArchive'],primary:'更新游戏',action:'download',secondary:[['刷新状态','refresh']],rule:'明确更新对象是游戏，区别于启动器自更新。'},
  downloading:{name:'下载中',kicker:'↓ 正在下载',title:'正在准备游戏文件',description:'下载完成后将校验文件完整性。',progress:42,meta:['演示进度'],primary:'暂停下载',action:'pause',secondary:[['停止','stop']],rule:'进度、速度、大小、剩余时间均可读；停止经过确认。'},
  paused:{name:'已暂停',kicker:'Ⅱ 已暂停',title:'下载已暂停',description:'可以继续当前下载，或停止本次操作。',progress:42,meta:['演示进度'],primary:'继续下载',action:'download',secondary:[['停止','stop']],rule:'暂停与停止是不同意图，不把停止描述为暂停。'},
  verifying:{name:'校验中',kicker:'◌ 正在校验',title:'正在核对游戏文件',description:'检查文件是否完整，请稍候。',progress:76,meta:['文件校验阶段'],primary:'正在校验…',disabled:true,secondary:[['停止','stop']],rule:'阶段名称与计量单位匹配；不提供暂停能力。'},
  ready:{name:'可启动',kicker:'✓ 已就绪',title:'游戏已准备就绪',description:'所有准备已完成，开始今天的日常吧。',meta:['蔚蓝档案 / 日服','D:\\Games\\BlueArchive'],primary:'开始游戏',action:'launch',split:true,rule:'主操作固定启动；更多菜单不改变主操作。'},
  launching:{name:'启动中',kicker:'◌ 正在启动',title:'正在启动游戏',description:'正在等待游戏进程启动。',meta:['游戏会话 / 启动中'],primary:'正在启动…',disabled:true,rule:'操作区继续显示会话状态，不允许重复启动。'},
  running:{name:'运行中',kicker:'✓ 游戏运行中',title:'祝你在基沃托斯玩得开心',description:'游戏正在运行。退出游戏后，这里会更新状态。',meta:['游戏会话 / 运行中'],primary:'游戏运行中',disabled:true,rule:'不新增未经实现支持的“切回游戏”或终止游戏操作。'},
  launchFailed:{name:'启动失败',kicker:'! 启动失败',title:'游戏未能成功启动',description:'无法确认游戏已启动。可以重试，或查看详情定位原因。',meta:['保留安装与配置'],primary:'重试启动',action:'launch',secondary:[['查看详情','error']],error:true,rule:'恢复可见窗口并给出原因与恢复入口。'},
  downloadFailed:{name:'下载失败',kicker:'! 下载未完成',title:'连接中断，下载未完成',description:'请检查网络连接后重试。可用的临时文件将在重试时检查。',meta:['演示原因 / 网络连接超时'],primary:'重试下载',action:'download',secondary:[['查看详情','error']],error:true,rule:'不承诺无条件续传；保留错误原因和重试入口。'}
};
const variants={A:{title:'A / 沉浸式首页',description:'壁纸铺开，状态与操作收拢到底部。日常打开时，首先看到游戏。',hypothesis:'重点观察：底部操作区是否稳定，人物是否被公告遮挡。'},B:{title:'B / 任务式首页',description:'右侧固定游戏操作区，左侧保留壁纸与动态。进行中的任务始终清晰。',hypothesis:'重点观察：侧栏是否有足够空间，壁纸裁切是否可以接受。'}};
const query=new URLSearchParams(location.search);
let variant=query.get('variant')==='B'?'B':'A';
let current=Object.hasOwn(states,query.get('state'))?query.get('state'):'ready';
let page=['map','states'].includes(query.get('page'))?query.get('page'):'preview';
let newsState=['loading','failed','empty','hidden'].includes(query.get('news'))?query.get('news'):'normal';
let returnFocus=null;
$('#scenario').innerHTML=Object.entries(states).map(([key,s])=>`<option value="${key}">${s.name}</option>`).join('');
$('#theme').value=query.get('theme')==='dark'?'dark':'light';
$('#news-state').value=newsState;
$('#state-table').innerHTML=Object.entries(states).map(([key,s])=>`<tr><td>${s.name}</td><td>${s.title}</td><td>${s.primary}${s.secondary?' / '+s.secondary.map(x=>x[0]).join('、'):''}</td><td>${s.rule}</td><td><button data-scenario="${key}">查看</button></td></tr>`).join('')+`<tr><td>公告失败</td><td>公告暂时不可用</td><td>重试公告</td><td>与游戏状态独立；仍允许启动已就绪的游戏。</td><td><button data-news-demo>查看</button></td></tr>`;
function syncUrl(){const p=new URLSearchParams({variant,state:current,theme:$('#theme').value,news:newsState,page});history.replaceState(null,'','?'+p);}
function setPage(next){page=next;document.querySelectorAll('.review-page').forEach(el=>el.hidden=el.id!==page);document.querySelectorAll('[data-page]').forEach(el=>{el.classList.toggle('active',el.dataset.page===page);el.setAttribute('aria-current',el.dataset.page===page?'page':'false');});$('.prototype-switcher').hidden=page!=='preview';syncUrl();}
function renderNews(){const host=$('#news');host.hidden=newsState==='hidden';let body='';
  if(newsState==='normal')body=`<button class="news-item" data-panel="news"><small>公告</small><span>游戏维护与更新通知</span></button><button class="news-item" data-panel="news"><small>动态</small><span>查看蔚蓝咖啡厅最新消息 ↗</span></button>`;
  else if(newsState==='loading')body=`<div class="news-state"><p>◌ 正在加载公告…</p></div>`;
  else if(newsState==='failed')body=`<div class="news-state"><strong>公告暂时不可用</strong><p>游戏操作不受影响。</p><button data-action="retry-news">重试公告</button></div>`;
  else body=`<div class="news-state"><p>暂无公告，有新消息时会显示在这里。</p></div>`;
  host.innerHTML=`<div class="news-header"><h3>公告与动态</h3><span>示例内容</span></div>${body}`;
}
function render(){const s=states[current],v=variants[variant];$('#app').dataset.variant=variant;$('#app').dataset.theme=$('#theme').value;$('#app').dataset.wallpaper=$('#wallpaper').value;$('#app').style.setProperty('--app-text',$('#text-size').value==='150'?'21px':'14px');$('#app').classList.toggle('grayscale',$('#grayscale').checked);$('#scenario').value=current;$('#variant-title').textContent=v.title;$('#variant-description').textContent=v.description;$('#hypothesis').textContent=v.hypothesis;
  ['A','B'].forEach(k=>{const b=$('#variant-'+k.toLowerCase());b.classList.toggle('active',k===variant);b.setAttribute('aria-pressed',k===variant);});
  const progress=s.progress?`<div class="progress-block"><div class="progress-track" role="progressbar" aria-label="${s.name}" aria-valuenow="${s.progress}" aria-valuemin="0" aria-valuemax="100"><div class="progress-fill" style="width:${s.progress}%"></div></div><div class="progress-meta">${current==='verifying'?'<span>760 / 1,000 个文件</span><span>76%</span>':`<span>4.2 / 10.0 GB</span><span>${current==='paused'?'已暂停':'24.8 MB/s · 约 4 分钟'}</span>`}</div></div>`:'';
  const primary=`<button class="primary-button" data-action="${s.action||'none'}" ${s.disabled?'disabled':''}>${s.primary}${s.action==='launch'?' &nbsp; ▷':''}</button>`;
  const actions=s.split?`<div class="split">${primary}<button class="menu-trigger" aria-label="游戏管理" aria-expanded="false" aria-controls="manage-menu" data-action="menu">⋯</button></div><div class="management-menu" id="manage-menu" hidden aria-label="游戏管理">${['检查更新','创建快捷方式','打开游戏文件夹','修复游戏','卸载游戏'].map(x=>`<button data-manage="${x}">${x}</button>`).join('')}</div>`:primary;
  $('#operation').innerHTML=`<div class="operation-info"><div class="status-kicker ${s.error?'error':s.disabled?'neutral':''}">${s.kicker}</div><h3 class="status-heading">${s.title}</h3><p class="status-description">${s.description}</p>${progress}<div class="operation-meta">${s.meta.map(x=>`<span class="${x.includes(':\\')?'path':''}">${x}</span>`).join('')}</div></div><div class="actions">${actions}${(s.secondary||[]).map(([label,action])=>`<button class="secondary-button" data-action="${action}">${label}</button>`).join('')}</div>`;
  renderNews();syncUrl();
}
function setState(next){const restore=$('#operation').contains(document.activeElement);current=next;render();if(restore){const target=$('#operation .primary-button:not(:disabled)')||$('#operation .status-heading');target.tabIndex=target.tagName==='BUTTON'?0:-1;target.focus();}}
function changeVariant(next){variant=next;render();}
function notify(message){$('#feedback').textContent=message;$('#feedback').hidden=false;clearTimeout(notify.timer);notify.timer=setTimeout(()=>$('#feedback').hidden=true,4200);}
function closeDialog(){ $('#detail-dialog').close();if(returnFocus?.isConnected)returnFocus.focus();else $('#scenario').focus(); }
function showDialog(title,content,buttons=[]){returnFocus=document.activeElement;$('#dialog-title').textContent=title;$('#dialog-content').innerHTML=content;$('#dialog-actions').innerHTML='';
  for(const b of buttons){const button=document.createElement('button');button.textContent=b.label;button.className=b.primary?'primary-button':'secondary-button';button.onclick=()=>{closeDialog();b.run?.();};$('#dialog-actions').append(button);}
  if(!buttons.length){const b=document.createElement('button');b.textContent='返回首页';b.onclick=closeDialog;$('#dialog-actions').append(b);}$('#detail-dialog').showModal();$('#close-dialog').focus();}
const panels={
  settings:['设置结构',`<p class="dialog-note">本轮只比较首页布局。以下为现有分类与职责，后续单独设计完整设置页；保存、取消及草稿保护继续保留。</p>${[['常规','语言、启动与关闭行为'],['游戏','路径与启动参数'],['下载与网络','下载源、并发与代理'],['外观','主题、壁纸与动态配色'],['高级','维护与诊断相关操作'],['关于','版本、更新与法律信息']].map(([a,b])=>`<div class="dialog-row"><strong>${a}</strong><span>${b}</span></div>`).join('')}`],
  resources:['资源面板结构',`<p class="dialog-note">资源偏好通过蔚蓝咖啡厅服务端读取与提交，不直接写入本地游戏资源目录。</p><div class="dialog-row"><strong>UID 与来源</strong><span>自动读取 / 手动输入</span></div><div class="dialog-row"><strong>资源偏好</strong><span>独立复选框，批量保存</span></div><div class="dialog-row"><strong>未保存状态</strong><span>关闭、刷新或切换前确认放弃</span></div>`],
  help:['帮助与诊断',`<p class="dialog-note">建议把帮助与日志入口集中组织。诊断面板仍保留现有 Debug 可见性约束。</p><div class="dialog-row"><strong>日志查看 / 导出</strong><span>只保存在本机</span></div><div class="dialog-row"><strong>独立崩溃窗口</strong><span>日志目录 / 复制详情 / 退出</span></div>`],
  news:['公告详情占位',`<p class="dialog-note">标题和内容仅用于比较信息密度，不代表真实活动或维护安排。生产实现继续使用现有远程内容与链接处理。</p>`]
};
function handleAction(action){
  if(action==='menu'){const menu=$('#manage-menu');menu.hidden=!menu.hidden;$('.menu-trigger').setAttribute('aria-expanded',String(!menu.hidden));if(!menu.hidden)menu.querySelector('button').focus();return;}
  if(action==='launch'){setState('launching');notify('已进入启动中。用上方状态选择器查看运行中或启动失败。');return;}
  if(action==='download'){setState('downloading');return;}
  if(action==='pause'){setState('paused');return;}
  if(action==='install'){showDialog('安装游戏',`<p>演示安装目标：<span class="path">D:\\Games\\BlueArchive</span></p><p class="dialog-note">此处只演示进入下载后的布局，不执行安装，也不模拟磁盘空间预检结果。</p>`,[{label:'取消'},{label:'演示下载',primary:true,run:()=>setState('downloading')}]);return;}
  if(action==='stop'){showDialog('停止当前操作？',`<p class="dialog-note">实际停止和临时文件处理继续由游戏操作域决定。此处只演示确认后的界面退出，不承诺停止后保留当前进度。</p>`,[{label:'继续当前操作'},{label:'演示停止',primary:true,run:()=>{setState('uninstalled');notify('演示操作已停止，回到安装入口。');}}]);return;}
  if(action==='retry-news'){newsState='normal';$('#news-state').value=newsState;renderNews();syncUrl();notify('演示公告已恢复。');return;}
  if(action==='refresh'){setState('checking');notify('检查结果由上方状态选择器模拟。');return;}
  if(action==='path'||action==='existing'){showDialog(action==='path'?'选择安装位置':'选择已有游戏目录',`<p class="dialog-note">生产界面在这里打开系统目录选择器。本原型不访问文件；可通过状态选择器查看安装或就绪结果。</p>`);return;}
  if(action==='error')showDialog('操作详情',`<p class="dialog-note">${states[current].description}</p><div class="dialog-row"><strong>错误来源</strong><span>演示数据，未读取真实日志</span></div><p style="margin-top:16px">生产实现应提供实际错误原因与日志入口，并保留重试能力。</p>`);
}
document.addEventListener('click',event=>{const b=event.target.closest('button');if(!b)return;
  if(b.dataset.page)setPage(b.dataset.page);
  if(b.dataset.scenario){setState(b.dataset.scenario);setPage('preview');}
  if(b.hasAttribute('data-news-demo')){setState('ready');newsState='failed';$('#news-state').value=newsState;render();setPage('preview');}
  if(b.dataset.action)handleAction(b.dataset.action);
  if(b.dataset.panel)showDialog(...panels[b.dataset.panel]);
  if(b.dataset.manage){$('#manage-menu').hidden=true;$('.menu-trigger').setAttribute('aria-expanded','false');showDialog(b.dataset.manage,`<p class="dialog-note">此原型只展示管理入口。${b.dataset.manage==='卸载游戏'?'实际卸载需要展示具体路径、整个目录的删除范围和可选附加范围，并再次确认。':'实际操作继续由现有游戏操作模块处理。'}</p>`);returnFocus=$('.menu-trigger');}
});
$('#scenario').onchange=e=>setState(e.target.value);
$('#news-state').onchange=e=>{newsState=e.target.value;renderNews();syncUrl();};
for(const id of ['theme','wallpaper','text-size','grayscale'])$('#'+id).onchange=render;
$('#variant-a').onclick=()=>changeVariant('A');$('#variant-b').onclick=()=>changeVariant('B');
$('#previous').onclick=$('#next').onclick=()=>changeVariant(variant==='A'?'B':'A');
$('#close-dialog').onclick=closeDialog;
$('#detail-dialog').addEventListener('cancel',event=>{event.preventDefault();closeDialog();});
document.addEventListener('keydown',event=>{if($('#detail-dialog').open)return;
  if(event.key==='Escape'&&$('#manage-menu')&&!$('#manage-menu').hidden){$('#manage-menu').hidden=true;$('.menu-trigger').setAttribute('aria-expanded','false');$('.menu-trigger').focus();return;}
  if(page!=='preview'||event.target.closest('input,select,textarea,[contenteditable="true"],dialog'))return;
  if(event.key==='ArrowLeft'||event.key==='ArrowRight'){event.preventDefault();changeVariant(variant==='A'?'B':'A');}
});
render();setPage(page);
