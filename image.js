'use strict';
const $=id=>document.getElementById(id),request=JotBridge.request;
const stage=$('imageStage'),canvas=$('imageCanvas'),fullImage=$('fullImage');
let zoom=1,fitMode=true,fullscreen=false,fullscreenBusy=false,drag=null,resizeFrame=0,copyStatusTimer=0;
function error(e){JotBridge.reportError(e,'image');$('imageError').textContent=JotI18n.text(e.message);$('imageError').hidden=false;}
function layout(){
  if(!fullImage.naturalWidth)return;
  const w=stage.clientWidth,h=stage.clientHeight;
  if(fitMode)zoom=Math.min(1,Math.max(.001,Math.min((w-32)/fullImage.naturalWidth,(h-32)/fullImage.naturalHeight)));
  const iw=fullImage.naturalWidth*zoom,ih=fullImage.naturalHeight*zoom;
  canvas.style.width=Math.max(w,iw+32)+'px';canvas.style.height=Math.max(h,ih+32)+'px';
  fullImage.style.width=iw+'px';fullImage.style.height=ih+'px';
  fullImage.style.left=Math.max(16,(w-iw)/2)+'px';fullImage.style.top=Math.max(16,(h-ih)/2)+'px';
  stage.dataset.fit=String(fitMode);stage.dataset.pannable=String(iw+32>w||ih+32>h);
  $('imageZoom').value=Math.round(zoom*100)+'%';$('zoomOut').disabled=zoom<=.1;$('zoomIn').disabled=zoom>=8;
  $('fitButton').ariaPressed=String(fitMode);$('actualButton').ariaPressed=String(!fitMode&&zoom===1);
}
function setZoom(value,x=stage.clientWidth/2,y=stage.clientHeight/2){
  if(!fullImage.naturalWidth)return;
  const px=(stage.scrollLeft+x-parseFloat(fullImage.style.left||0))/zoom,py=(stage.scrollTop+y-parseFloat(fullImage.style.top||0))/zoom;
  fitMode=false;zoom=Math.min(8,Math.max(.1,value));layout();
  stage.scrollLeft=px*zoom+parseFloat(fullImage.style.left)-x;stage.scrollTop=py*zoom+parseFloat(fullImage.style.top)-y;
}
function fit(value){if(!value){setZoom(1);return;}fitMode=true;stage.scrollTo(0,0);layout();}
async function setFullscreen(value=!fullscreen){
  if(fullscreenBusy)return;fullscreenBusy=true;$('imageFullscreen').disabled=true;
  try{fullscreen=await request('image-fullscreen',value);const b=$('imageFullscreen');b.ariaPressed=String(fullscreen);b.ariaLabel=fullscreen?'Exit fullscreen':'Enter fullscreen';b.title=fullscreen?'Exit fullscreen':'Fullscreen';b.replaceChildren(JotDesign.icon(fullscreen?'minimize':'maximize'));layout();}
  catch(e){error(e);}finally{fullscreenBusy=false;$('imageFullscreen').disabled=false;}
}
$('fitButton').onclick=()=>fit(true);$('actualButton').onclick=()=>fit(false);
$('zoomIn').onclick=()=>setZoom(zoom*1.25);$('zoomOut').onclick=()=>setZoom(zoom/1.25);
$('imageFullscreen').onclick=()=>setFullscreen();$('imageClose').onclick=()=>request('hide').catch(error);
async function copyImage(){
  const b=$('imageCopy'),status=$('imageCopyStatus');if(b.disabled)return;
  clearTimeout(copyStatusTimer);b.disabled=true;b.ariaBusy='true';b.title='Copying original…';status.textContent='Copying original…';status.hidden=false;
  try{
    if(await request('clipboard-write',{html:'<img src="'+fullImage.src+'">',text:'',image:fullImage.src})===false)throw new Error('Copy was interrupted. Please try again.');
    status.textContent='Copied';copyStatusTimer=setTimeout(()=>status.hidden=true,1100);
  }catch(e){status.hidden=true;error(e);}
  finally{b.disabled=false;b.removeAttribute('aria-busy');b.title='Copy original image';}
}
$('imageCopy').onclick=copyImage;
function openImageMenu(x,y){
  JotMenus.open({x,y,owner:stage,restore:()=>stage.focus({preventScroll:true}),error,items:[
    {id:'copy',label:'Copy image',icon:'copy',disabled:$('imageCopy').disabled,pending:'Copying…'},
    {separator:true},{id:'zoom-in',label:'Zoom in',icon:'plus',disabled:zoom>=8},
    {id:'zoom-out',label:'Zoom out',icon:'minus',disabled:zoom<=.1},
    {id:'fit',label:'Fit to window',icon:'scan'},
    {id:'actual',label:'100%',icon:'scan'},
    {separator:true},{id:'fullscreen',label:fullscreen?'Exit fullscreen':'Fullscreen',icon:fullscreen?'minimize':'maximize'}
  ],async run(action){
    if(action==='copy')await copyImage();
    else if(action==='zoom-in')setZoom(zoom*1.25);
    else if(action==='zoom-out')setZoom(zoom/1.25);
    else if(action==='fit')fit(true);else if(action==='actual')fit(false);
    else if(action==='fullscreen')await setFullscreen();
  }});
}
stage.tabIndex=0;stage.setAttribute('aria-label','Image view');
fullImage.addEventListener('contextmenu',event=>{event.preventDefault();openImageMenu(event.clientX,event.clientY);});
stage.addEventListener('keydown',event=>{if(event.key==='ContextMenu'||event.shiftKey&&event.key==='F10'){event.preventDefault();event.stopPropagation();const r=stage.getBoundingClientRect();openImageMenu(r.left+16,r.top+16);}});
stage.addEventListener('wheel',e=>{e.preventDefault();const r=stage.getBoundingClientRect();setZoom(zoom*Math.exp(-Math.sign(e.deltaY)*.16),e.clientX-r.left,e.clientY-r.top);},{passive:false});
stage.addEventListener('dblclick',()=>fit(!fitMode));
stage.addEventListener('pointerdown',e=>{if(e.button!==0||stage.dataset.pannable!=='true')return;e.preventDefault();drag={x:e.clientX,y:e.clientY,left:stage.scrollLeft,top:stage.scrollTop};stage.setPointerCapture(e.pointerId);stage.dataset.dragging='true';});
stage.addEventListener('pointermove',e=>{if(drag){stage.scrollLeft=drag.left+drag.x-e.clientX;stage.scrollTop=drag.top+drag.y-e.clientY;}});
function endDrag(){drag=null;stage.dataset.dragging='false';}
stage.addEventListener('pointerup',endDrag);stage.addEventListener('lostpointercapture',endDrag);stage.addEventListener('pointercancel',endDrag);
new ResizeObserver(()=>{cancelAnimationFrame(resizeFrame);resizeFrame=requestAnimationFrame(layout);}).observe(stage);
JotBridge.on(data=>{if(data.event==='preferences')JotDesign.apply(data.prefs);if(data.event==='clipboard-success')$('imageError').hidden=true;});
(async()=>{
  JotDesign.icons();JotDesign.drag($('imageHandle'));const context=await request('context');JotDesign.apply(await request('preferences-load'));
  fullImage.onload=()=>{$('imageDimensions').textContent=fullImage.naturalWidth+' × '+fullImage.naturalHeight+' · Scroll to zoom · Drag to pan';layout();window.jotReady=true;};
  fullImage.onerror=()=>error(new Error('This image could not be opened. Your note is unchanged.'));fullImage.src=context.image;
  if(fullImage.complete&&fullImage.naturalWidth>0)fullImage.onload();
})().catch(error);
