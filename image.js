'use strict';
const $=id=>document.getElementById(id),request=JotBridge.request;
function error(e){JotBridge.reportError(e,'image');$('imageError').textContent=JotI18n.text(e.message);$('imageError').hidden=false;}
function fit(value){$('imageStage').dataset.fit=String(value);}
$('fitButton').onclick=()=>fit(true);$('actualButton').onclick=()=>fit(false);
$('imageClose').onclick=()=>request('hide').catch(error);
$('imageCopy').onclick=()=>request('clipboard-write',{html:'<img src="'+$('fullImage').src+'">',text:'',image:$('fullImage').src}).catch(error);
document.addEventListener('keydown',event=>{if(event.key==='Escape')request('hide').catch(error);});
JotBridge.on(data=>{if(data.event==='preferences')JotDesign.apply(data.prefs);if(data.event==='clipboard-success')$('imageError').hidden=true;});
(async()=>{
  JotDesign.icons();JotDesign.drag($('imageHandle'));
  const context=await request('context');
  JotDesign.apply(await request('preferences-load'));
  const image=$('fullImage');
  image.onload=()=>{$('imageDimensions').textContent=image.naturalWidth+' × '+image.naturalHeight;window.jotReady=true;};
  image.onerror=()=>error(new Error('This image could not be opened. Your note is unchanged.'));
  image.src=context.image;
  if(image.complete&&image.naturalWidth>0)image.onload();
})().catch(error);
