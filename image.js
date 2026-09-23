'use strict';
const $=id=>document.getElementById(id),request=JotBridge.request;
function error(e){JotBridge.reportError(e,'image');$('imageError').textContent=JotI18n.text(e.message);$('imageError').hidden=false;}
function fit(value){$('imageStage').dataset.fit=String(value);}
$('fitButton').onclick=()=>fit(true);$('actualButton').onclick=()=>fit(false);
$('imageClose').onclick=()=>request('hide').catch(error);
$('imageCopy').onclick=()=>request('clipboard-write',{html:'<img src="'+$('fullImage').src+'">',text:'',image:$('fullImage').src}).catch(error);
document.addEventListener('keydown',event=>{if(event.key==='Escape')request('hide').catch(error);});
JotBridge.on(data=>{if(data.event==='preferences')JotDesign.apply(data.prefs);if(data.event==='clipboard-success')$('imageError').hidden=true;});
(async()=>{JotDesign.icons();JotDesign.drag($('imageHandle'));const context=await request('context');const data=await request('load');JotDesign.apply(data?.prefs||{});$('fullImage').src=context.image;$('fullImage').onload=()=>{$('imageDimensions').textContent=$('fullImage').naturalWidth+' × '+$('fullImage').naturalHeight;window.jotReady=true;};})().catch(error);
