/* Mock interaction only. All state is in memory. No launcher or filesystem operations. */
const $ = id => document.getElementById(id);
const icon = name => `<svg aria-hidden="true"><use href="#i-${name}"/></svg>`;
const params = new URLSearchParams(location.search);
const validStates = [...$('state-select').options].map(o => o.value);
const taskNames = {install:'安装游戏',update:'更新游戏',repair:'修复游戏'};
const verbs = {install:'安装',update:'更新',repair:'修复'};
const state = {
  task: Object.hasOwn(taskNames, params.get('task')) ? params.get('task') : 'update',
  scenario: validStates.includes(params.get('state')) ? params.get('state') : 'downloading',
  theme: params.get('theme') === 'dark' ? 'dark' : 'light',
  size: params.get('size') === 'compact' ? 'compact' : 'default',
  scale: params.get('scale') === '150' ? '150' : '100',
  details: params.get('details') === '1', percent:64, playing:false,
  background:false, settings:false, pendingNotice:false, toast:false, menu:false
};
let ticker, feedbackTimer, stoppingTimer;
const resultStates = new Set(['success','uptodate','failed','stopped']);
const phaseIndexes = {preparing:0,checking:1,downloading:2,downloadcheck:2,retrying:2,paused:2,stalled:2,verifying:3,finalizing:4,stopping:2,success:5,uptodate:5,failed:2,stopped:2};
const defaultPercents = {preparing:0,checking:36,downloading:64,downloadcheck:100,verifying:43,finalizing:0,retrying:28,paused:64,stalled:64,stopping:64,success:100,uptodate:100,failed:64,stopped:64,idle:0};
const reviewNotes = {
  preparing:'等待清单时使用不定进度，不显示虚假的百分比。',
  checking:'检查阶段按文件计数；修复任务在这里扫描缺失或损坏文件。',
  downloading:'当前阶段进度，不代表整个更新任务的完成度。',
  downloadcheck:'下载字节到 100% 后，仍明确显示下载文件的检查状态。',
  verifying:'校验按文件计数，并在详情中显示并行文件的真实读取进度。',
  finalizing:'应用文件、保存状态与刷新结束后，才进入成功结果。',
  retrying:'重新下载使用本轮进度；重试轮次放在简短的次要信息中。',
  paused:'保留已有完成量，暂停时隐藏下载速度与预计剩余时间。',
  stalled:'30 秒无实际推进时给出提示；恢复推进后提示自动消失。',
  stopping:'清理结束前保留任务上下文，阻止重复停止和新任务。',
  success:'结果保留在原位，由用户主动开始游戏或关闭结果。',
  uptodate:'没有差异时跳过下载和校验，直接展示检查结果。',
  failed:'保留任务上下文与已完成量，提供重试和日志入口。',
  stopped:'用户停止是独立终态，可以重新开始任务。',
  idle:'关闭结果后恢复操作面板，壁纸重新获得更多空间。'
};

function syncUrl(){
  const q=new URLSearchParams();
  q.set('task',state.task); q.set('state',state.scenario); q.set('theme',state.theme);
  if(state.size==='compact')q.set('size','compact');
  if(state.scale==='150')q.set('scale','150');
  if(state.details)q.set('details','1');
  try{history.replaceState(null,'',`${location.pathname}?${q}`);}catch{/* file:// preview still works */}
}
function showFeedback(text){
  $('review-feedback').textContent=text;$('review-feedback').hidden=false;
  clearTimeout(feedbackTimer);feedbackTimer=setTimeout(()=>{$('review-feedback').hidden=true;},3500);
}
function stopPlayback(){state.playing=false;clearInterval(ticker);$('demo-button').innerHTML=icon('play')+'<span>播放流程</span>';}
function setScenario(name,{keepPercent=false}={}){
  const wasResult=resultStates.has(state.scenario);
  state.scenario=name;
  state.menu=false;
  if(!keepPercent)state.percent=defaultPercents[name]??0;
  if(!resultStates.has(name)){state.toast=false;state.pendingNotice=false;}
  if(!wasResult&&resultStates.has(name)){
    if(state.background)state.pendingNotice=true;
    else if(state.settings)state.toast=true;
  }
  render();syncUrl();
}
function actionButton(id,label,symbol,action,visible=true){
  const b=$(id);b.hidden=!visible;b.dataset.action=action;
  b.setAttribute('aria-label',label);b.title=label;
  b.innerHTML=(symbol?icon(symbol):'')+`<span>${label}</span>`;
}
function render(){
  const s=state.scenario, verb=verbs[state.task], result=resultStates.has(s), phase=phaseIndexes[s]??0;
  const check=s==='checking',verify=s==='verifying',download=['downloading','stalled','paused','retrying','downloadcheck'].includes(s);
  const indeterminate=['preparing','finalizing','stopping','downloadcheck'].includes(s);
  $('task-select').value=state.task;$('state-select').value=s;$('theme-select').value=state.theme;$('size-select').value=state.size;$('scale-select').value=state.scale;
  $('launcher').dataset.theme=state.theme;$('launcher').dataset.size=state.size;$('launcher').style.setProperty('--type-scale',Number(state.scale)/100);
  for(const dialog of document.querySelectorAll('.dialog'))dialog.dataset.theme=state.theme;
  $('task-card').className='task-card'+(result?' is-result':'')+(s==='success'||s==='uptodate'?' is-success':` is-${s}`);
  $('task-card').hidden=s==='idle';$('idle-panel').hidden=false;
  $('task-name').textContent=taskNames[state.task];$('task-symbol').innerHTML=`<use href="#i-${state.task==='install'?'download':state.task==='repair'?'scan':'refresh'}"/>`;
  $('task-context').textContent=s==='retrying'?'自动重试，第 1 轮':'Blue Archive JP';
  const badges={paused:'已暂停',stalled:'等待进展',stopping:'正在停止',retrying:'自动重试',success:'已完成',uptodate:'无需更改',failed:'任务失败',stopped:'已停止'};
  $('task-badge').textContent=badges[s]||'进行中';
  const stages=['准备','检查','下载','校验','收尾'];
  $('stage-nav').innerHTML=stages.map((label,i)=>{
    const skip=s==='uptodate'&&[2,3].includes(i);
    const done=i<phase&&!skip,current=i===phase&&(!result||s==='failed'||s==='stopped');
    return `<li class="${skip?'skipped':done?'done':current?'current':''}"${current?' aria-current="step"':''}><span class="step-number">${skip?'—':done?icon('check'):i+1}</span><span>${label}${skip?'<span class="skip-label">跳过</span>':''}</span></li>`;
  }).join('');
  let title='正在下载游戏文件',description='下载完成后，将自动校验文件。';
  if(s==='preparing'){title='正在准备'+verb;description='正在获取游戏文件清单，请稍候。';}
  if(check){title=state.task==='repair'?'正在检查缺失或损坏文件':'正在检查本地文件';description=state.task==='repair'?'逐一检查文件，确定需要修复的内容。':'正在确定本次需要下载的内容。';}
  if(verify){title='正在校验游戏文件';description='确认文件完整后，将应用文件并完成收尾。';}
  if(s==='downloadcheck'){title='下载完成，正在检查文件';description='文件检查仍在进行，之后将进入完整校验。';}
  if(s==='finalizing'){title='正在完成'+verb;description='正在应用文件、保存状态并刷新游戏信息。';}
  if(s==='retrying'){title='正在重新下载未通过校验的文件';description='自动重试第 1 轮，完成后将再次校验。';}
  if(s==='paused'){title='下载已暂停';description='继续后将从当前进度恢复下载。';}
  if(s==='stopping'){title='正在停止'+verb;description='正在结束任务并清理，请稍候。';}
  if(s==='success'){title=verb+'完成';description='游戏文件已就绪，可以开始游戏。';}
  if(s==='uptodate'){title=state.task==='repair'?'游戏文件完整':'游戏已是最新版本';description='检查完成，无需下载或更改游戏文件。';}
  if(s==='failed'){title=verb+'未完成';description='下载连接中断，已完成的进度保留在此。';}
  if(s==='stopped'){title=verb+'已停止';description='任务已结束，清理完成。可以重新开始。';}
  const shortTitles={preparing:'正在准备'+verb,checking:'正在检查文件',downloading:'正在下载',downloadcheck:'正在检查下载文件',verifying:'正在校验',finalizing:'正在完成'+verb,retrying:'重新下载',paused:'下载已暂停',stalled:'暂未检测到新进展',stopping:'正在停止',success:verb+'完成',uptodate:state.task==='repair'?'文件完整':'已是最新版本',failed:verb+'失败',stopped:verb+'已停止'};
  $('phase-title').innerHTML=(result?icon(s==='failed'?'warning':s==='stopped'?'stop':'check'):'')+(shortTitles[s]||title);
  $('phase-description').textContent=description;
  const phaseLabel=check?'检查进度':verify?'校验进度':'下载进度';
  $('percent-label').textContent=phaseLabel;$('percent').textContent=state.percent;
  $('percent-group').hidden=indeterminate||result;
  $('progressbar').hidden=result;$('progressbar').className='progress-track'+(indeterminate?' indeterminate':'');
  $('progressbar').setAttribute('aria-label',indeterminate?title:phaseLabel);
  if(indeterminate)$('progressbar').removeAttribute('aria-valuenow');else $('progressbar').setAttribute('aria-valuenow',state.percent);
  $('progress-fill').style.width=state.percent+'%';
  const processed=Math.round(state.percent/100*1842);
  $('amount').innerHTML=check||verify?`已${check?'检查':'校验'} <strong>${processed.toLocaleString('en-US')}</strong><span> / 1,842 个文件</span>`:s==='retrying'?`已重新下载 <strong>${(state.percent/100*240).toFixed(1)} MB</strong><span> / 240 MB，3 个文件</span>`:`已下载 <strong>${(state.percent/100*6).toFixed(2)} GB</strong><span> / 6.00 GB</span>`;
  $('amount').hidden=indeterminate||s==='success'||s==='uptodate';
  $('download-facts').hidden=!['downloading','stalled','retrying'].includes(s);
  $('speed').textContent=s==='stalled'?'0 B/s':s==='retrying'?'12.8 MB/s':'18.6 MB/s';
  $('eta').textContent=s==='stalled'?'估算中':s==='retrying'?'约 15 秒':'约 2 分钟';
  $('phase-fact').hidden=!check&&!verify&&s!=='paused';
  $('phase-fact').textContent=s==='paused'?'继续后恢复速度与时间估算':check?'正在读取文件内容':'8 个文件正在校验';
  $('primary-action').disabled=s==='stopping';
  if(s==='paused')actionButton('primary-action','继续下载','play','resume');
  else if(['success','uptodate'].includes(s))actionButton('primary-action','开始游戏','play','launch',false);
  else if(s==='failed')actionButton('primary-action','重试','refresh','retry');
  else if(s==='stopped')actionButton('primary-action','重新'+verb,'refresh','restart');
  else actionButton('primary-action','暂停下载','pause','pause',download&&s!=='downloadcheck');
  if(s==='failed')actionButton('secondary-action','查看日志','log','log');
  else actionButton('secondary-action',s==='stopping'?'正在停止':'停止任务','stop','stop',!result);
  $('secondary-action').disabled=s==='stopping';
  $('dismiss-action').hidden=!result;
  const noticeText=s==='stalled'?'暂未检测到新进展。已连续 30 秒没有新的处理进度，可以继续等待或停止任务。':s==='failed'?'无法连接到下载服务器。请检查网络连接后重试。':s==='retrying'?'3 个文件未通过校验，正在重新下载。此前已完成的检查仍会用于后续处理。':'';
  $('notice').hidden=!noticeText;$('notice-text').textContent=noticeText;
  $('detail-section').hidden=!state.details;
  $('detail-actions').hidden=!state.details;
  $('detail-log-action').hidden=s==='failed';
  $('detail-summary').textContent=verify?'8 个文件正在校验':check?'正在检查文件':s==='stopping'?'等待处理结束':s==='downloadcheck'?'正在检查已下载文件':s==='retrying'?'3 个文件正在处理':s==='paused'?'已暂停':'10 个文件正在处理';
  $('source-label').textContent=verify||check?'本地文件':s==='retrying'?'本轮需重下 3 个文件':'Cafe 下载源';
  $('details-button').setAttribute('aria-expanded',String(state.details));
  $('details-button').setAttribute('aria-label',state.details?'收起任务详情':'展开任务详情');
  $('details-button').title=state.details?'收起任务详情':'展开任务详情';
  const hasFiles=!result&&!['preparing','finalizing'].includes(s);
  $('file-details').hidden=!state.details||!hasFiles;
  $('launcher').querySelector('.file-detail-caption').hidden=!hasFiles;
  const summaries={
    preparing:['获取游戏文件清单',''],
    checking:[`${processed.toLocaleString('en-US')} / 1,842 个文件`,''],
    downloading:['18.6 MB/s','下载剩余约 2 分钟'],
    downloadcheck:['下载完成，文件检查仍在进行',''],
    verifying:[`${processed.toLocaleString('en-US')} / 1,842 个文件`,''],
    finalizing:['应用文件并保存状态',''],
    retrying:['第 1 轮自动重试','下载剩余约 15 秒'],
    paused:[`${(state.percent/100*6).toFixed(2)} / 6.00 GB`,''],
    stalled:['30 秒没有新进展','可继续等待'],
    stopping:['正在结束任务并清理',''],
    success:['游戏已就绪',''],uptodate:['无需下载或更改文件',''],
    failed:['下载连接中断，可重试',''],stopped:['清理已完成','']
  };
  $('summary-left').textContent=summaries[s]?.[0]||'';
  $('summary-right').textContent=summaries[s]?.[1]||'';
  const ready=s==='idle'||s==='success'||s==='uptodate';
  $('dock-status').textContent=ready?'游戏已就绪':result?verb+'任务已结束':verb+'任务进行中';
  const activeTaskLabels={install:'正在下载',update:'正在更新',repair:'正在修复'};
  const splitLabels={
    preparing:'准备'+verb,checking:'正在检查',downloading:activeTaskLabels[state.task],
    downloadcheck:'正在检查',verifying:'正在校验',finalizing:'正在'+verb,
    retrying:'正在重试',paused:'下载暂停',stalled:'等待进展',stopping:'正在停止',
    failed:verb+'失败',stopped:verb+'已停止'
  };
  const splitLabel=ready?'开始游戏':splitLabels[s];
  $('split-label').textContent=splitLabel;
  $('split-main').setAttribute('aria-label',splitLabel);
  $('split-main').title=ready?'开始游戏':splitLabel+'，查看任务详情';
  $('split-main').dataset.action=ready?'launch':'show-task';
  const splitIcons={paused:'pause',checking:'scan',downloadcheck:'scan',verifying:'scan',retrying:'refresh',stalled:'info',stopping:'stop',failed:'warning',stopped:'stop'};
  const splitIcon=ready?'play':splitIcons[s]||(state.task==='install'?'download':state.task==='repair'?'scan':'refresh');
  $('split-icon').innerHTML=`<use href="#i-${splitIcon}"/>`;
  $('game-menu').hidden=!state.menu;
  $('split-menu-toggle').setAttribute('aria-expanded',String(state.menu));
  renderFiles(check,verify);
  $('state-caption').textContent=$('state-select').selectedOptions[0].textContent;
  $('review-note').textContent=reviewNotes[s];
  $('background-cover').hidden=!state.background;$('settings-cover').hidden=!state.settings;
  const obscured=state.background||state.settings;
  for(const id of ['task-card','idle-panel'])$(id).inert=obscured;
  $('launcher').querySelector('.titlebar').inert=obscured;
  $('launcher').querySelector('.wallpaper-space').inert=obscured;
  const toastTitle=s==='success'?verb+'完成':s==='uptodate'?'检查完成':s==='failed'?verb+'未完成':verb+'已停止';
  $('toast-title').textContent=toastTitle;$('toast-body').textContent=description;
  $('toast').hidden=!state.toast||state.background;
  $('toast').querySelector('.toast-icon').innerHTML=icon(s==='failed'?'warning':s==='stopped'?'stop':'check');
}
function renderFiles(check,verify){
  const s=state.scenario;
  const names=['GameData/Audio/JP/Voice/CharacterVoice_10001.bundle','GameData/Media/Scenario/Scenario_Main_Chapter01.bundle','GameData/Texture/Character/Character_10001_Atlas.bundle'];
  const percents=[73,42,91];
  const count=check?1:3;
  $('active-files').innerHTML=names.slice(0,count).map((name,i)=>{
    const reading=check||verify||s==='downloadcheck'||i===2;
    const activity=s==='paused'&&!reading?'已暂停':s==='stalled'?'等待进展':s==='stopping'?'正在结束':reading?'正在校验':'正在下载';
    return `<div class="file-row">${icon('file')}<div class="file-name" title="${name}">${name}<small>${activity}</small></div><div class="file-meter"><span>${reading?'已读取 ':''}${(percents[i]/100*[512,128,64][i]).toFixed(1)} / ${[512,128,64][i]} MB</span><progress value="${percents[i]}" max="100" aria-label="${name} ${activity}">${percents[i]}%</progress></div></div>`;
  }).join('');
  $('more-files').textContent=check?'已处理的文件会计入上方检查进度。':verify?'另有 5 个文件正在校验。':s==='retrying'?'本轮正在重新下载并检查 3 个文件。':s==='downloadcheck'?'正在等待已下载文件的检查结束。':s==='paused'?'下载已暂停。正在执行的文件检查可能继续完成。':'另有 7 个文件正在处理。';
}
function playDemo(){
  if(state.playing){stopPlayback();return;}
  clearTimeout(stoppingTimer);state.playing=true;setScenario('preparing');
  $('demo-button').innerHTML=icon('pause')+'<span>暂停演示</span>';
  let ticks=0;
  ticker=setInterval(()=>{
    ticks++;
    if(ticks===2)setScenario('checking');
    else if(ticks>2&&ticks<6){state.percent=Math.min(100,36+(ticks-2)*20);render();}
    else if(ticks===6)setScenario('downloading');
    else if(ticks>6&&ticks<12){state.percent=Math.min(100,64+(ticks-6)*7);render();}
    else if(ticks===12)setScenario('downloadcheck');
    else if(ticks===14)setScenario('verifying');
    else if(ticks>14&&ticks<19){state.percent=Math.min(100,43+(ticks-14)*14);render();}
    else if(ticks===19)setScenario('finalizing');
    else if(ticks===22){setScenario('success');stopPlayback();}
  },800);
}
$('task-select').addEventListener('change',e=>{state.task=e.target.value;render();syncUrl();});
$('state-select').addEventListener('change',e=>{stopPlayback();clearTimeout(stoppingTimer);setScenario(e.target.value);});
for(const [id,key] of [['theme-select','theme'],['size-select','size'],['scale-select','scale']])$(id).addEventListener('change',e=>{state[key]=e.target.value;render();syncUrl();});
$('details-button').addEventListener('click',()=>{state.details=!state.details;render();syncUrl();});
$('demo-button').addEventListener('click',playDemo);
$('clean-button').addEventListener('click',()=>{document.body.classList.add('clean');$('exit-clean').hidden=false;});
$('exit-clean').addEventListener('click',()=>{document.body.classList.remove('clean');$('exit-clean').hidden=true;});
document.addEventListener('keydown',e=>{if(e.key==='Escape'){document.body.classList.remove('clean');$('exit-clean').hidden=true;if(state.settings){state.settings=false;render();}else if(state.background){restore();}else if(state.menu){state.menu=false;render();$('split-menu-toggle').focus();}}});
function restore(){state.background=false;if(state.pendingNotice){state.toast=true;state.pendingNotice=false;}render();}
document.addEventListener('click',e=>{
  const button=e.target.closest('[data-action]');if(!button)return;
  const a=button.dataset.action;
  if(button.closest('#game-menu')){state.menu=false;render();}
  if(a==='game-menu'){state.menu=!state.menu;render();}
  if(a==='show-task'){state.menu=false;state.details=true;render();syncUrl();}
  if(a==='game-folder')showFeedback('演示：打开游戏目录。设计稿不会访问实际目录。');
  if(a==='pause'){stopPlayback();setScenario('paused',{keepPercent:true});}
  if(a==='resume'){setScenario('downloading',{keepPercent:true});}
  if(a==='stop'){$('stop-title').textContent='停止'+verbs[state.task]+'？';$('stop-dialog').returnValue='';$('stop-dialog').showModal();}
  if(a==='dismiss'){stopPlayback();setScenario('idle');}
  if(a==='retry'||a==='restart'){stopPlayback();setScenario('preparing');showFeedback('演示：重新检查当前状态与文件，随后开始任务。');}
  if(a==='launch')showFeedback('演示：由你主动启动游戏。设计稿不会运行游戏。');
  if(a==='log'){$('log-content').textContent=`[设计稿演示记录]\n任务：${taskNames[state.task]}\n目录：D:\\Games\\BlueArchive\n当前状态：${$('state-select').selectedOptions[0].textContent}\n\n[INFO] 游戏文件清单读取完成\n[INFO] 需要下载：6.00 GB\n[INFO] 当前轮次：${state.scenario==='retrying'?'自动重试 1':'初次执行'}\n${state.scenario==='failed'?'[ERROR] 下载连接中断，等待用户重试':'[INFO] 文件处理进行中'}`;$('log-dialog').showModal();}
  if(a==='close-log')$('log-dialog').close();
  if(a==='settings'){state.settings=true;render();}
  if(a==='close-settings'){state.settings=false;render();}
  if(a==='resources')showFeedback('资源面板入口保留。本设计稿聚焦游戏任务进度。');
  if(a==='resize'){state.size=state.size==='compact'?'default':'compact';render();syncUrl();}
  if(a==='minimize'||a==='window-close'){state.background=true;render();}
  if(a==='restore')restore();
  if(a==='view-result'){state.toast=false;state.settings=false;state.background=false;render();$('task-card').scrollIntoView({block:'nearest'});}
  if(a==='close-toast'){state.toast=false;render();}
});
$('stop-dialog').addEventListener('close',()=>{if($('stop-dialog').returnValue==='stop'){stopPlayback();setScenario('stopping');stoppingTimer=setTimeout(()=>setScenario('stopped'),1800);}});
state.percent=defaultPercents[state.scenario]??64;
render();syncUrl();
