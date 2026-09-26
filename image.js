'use strict';
const $=id=>document.getElementById(id),request=JotBridge.request;
const stage=$('imageStage'),canvas=$('imageCanvas'),fullImage=$('fullImage');
let zoom=1,fitMode=true,fullscreen=false,fullscreenBusy=false,drag=null,resizeFrame=0;
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
  try{fullscreen=await request('image-fullscreen',value);const b=$('imageFullscreen');b.ariaPressed=String(fullscreen);b.ariaLabel=fullscreen?'Exit fullscreen':'Enter fullscreen';b.title=fullscreen?'Exit fullscreen · Esc':'Fullscreen · F11';b.replaceChildren(JotDesign.icon(fullscreen?'minimize':'maximize'));layout();}
  catch(e){error(e);}finally{fullscreenBusy=false;$('imageFullscreen').disabled=false;}
}
$('fitButton').onclick=()=>fit(true);$('actualButton').onclick=()=>fit(false);
$('zoomIn').onclick=()=>setZoom(zoom*1.25);$('zoomOut').onclick=()=>setZoom(zoom/1.25);
$('imageFullscreen').onclick=()=>setFullscreen();$('imageClose').onclick=()=>request('hide').catch(error);
$('imageCopy').onclick=async()=>{const b=$('imageCopy'),label=$('imageDimensions'),previous=label.textContent;b.disabled=true;b.ariaBusy='true';b.title='Copying original…';label.textContent='Copying original…';try{if(await request('clipboard-write',{html:'<img src="'+fullImage.src+'">',text:'',image:fullImage.src})===false)throw new Error('Copy was interrupted. Please try again.');}catch(e){error(e);}finally{b.disabled=false;b.removeAttribute('aria-busy');b.title='Copy original image';label.textContent=previous;}};
stage.addEventListener('wheel',e=>{e.preventDefault();const r=stage.getBoundingClientRect();setZoom(zoom*Math.exp(-Math.sign(e.deltaY)*.16),e.clientX-r.left,e.clientY-r.top);},{passive:false});
stage.addEventListener('dblclick',()=>fit(!fitMode));
stage.addEventListener('pointerdown',e=>{if(e.button!==0||stage.dataset.pannable!=='true')return;e.preventDefault();drag={x:e.clientX,y:e.clientY,left:stage.scrollLeft,top:stage.scrollTop};stage.setPointerCapture(e.pointerId);stage.dataset.dragging='true';});
stage.addEventListener('pointermove',e=>{if(drag){stage.scrollLeft=drag.left+drag.x-e.clientX;stage.scrollTop=drag.top+drag.y-e.clientY;}});
function endDrag(){drag=null;stage.dataset.dragging='false';}
stage.addEventListener('pointerup',endDrag);stage.addEventListener('lostpointercapture',endDrag);stage.addEventListener('pointercancel',endDrag);
document.addEventListener('keydown',e=>{
  if(e.key==='F11'){e.preventDefault();setFullscreen();}
  else if(e.key==='Escape'){e.preventDefault();if(fullscreen)setFullscreen(false);else request('hide').catch(error);}
  else if(['+','=','-','0','1'].includes(e.key)){e.preventDefault();if(e.key==='0')fit(true);else if(e.key==='1')fit(false);else setZoom(zoom*(e.key==='-'?.8:1.25));}
});
new ResizeObserver(()=>{cancelAnimationFrame(resizeFrame);resizeFrame=requestAnimationFrame(layout);}).observe(stage);
JotBridge.on(data=>{if(data.event==='preferences')JotDesign.apply(data.prefs);if(data.event==='clipboard-success')$('imageError').hidden=true;});
(async()=>{
  JotDesign.icons();JotDesign.drag($('imageHandle'));const context=await request('context');JotDesign.apply(await request('preferences-load'));
  fullImage.onload=()=>{$('imageDimensions').textContent=fullImage.naturalWidth+' × '+fullImage.naturalHeight+' · Scroll to zoom · Drag to pan';layout();window.jotReady=true;};
  fullImage.onerror=()=>error(new Error('This image could not be opened. Your note is unchanged.'));fullImage.src=context.image;
  if(fullImage.complete&&fullImage.naturalWidth>0)fullImage.onload();
})().catch(error);
