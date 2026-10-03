'use strict';
window.JotShortcutBindings=(()=>{
  let config=null;
  function label(chord){return chord?chord.replace(/Key|Digit/g,'').replace('Comma',',').replace('Period','.').replace('Slash','/').replace('Semicolon',';').replace('Quote',"'").replace('BracketLeft','[').replace('BracketRight',']').replace('Backslash','\\').replace('Minus','−').replace('Equal','='):'';}
  function chordFor(event){
    if(event.isComposing||event.keyCode===229||event.metaKey||event.getModifierState?.('AltGraph'))return null;
    let code=event.code;
    if(!code)code={',':'Comma',Tab:'Tab'}[event.key]||(/^\d$/.test(event.key)?'Digit'+event.key:/^[a-z]$/i.test(event.key)?'Key'+event.key.toUpperCase():event.key);
    if(!config?.keys.includes(code))return null;
    return (event.ctrlKey?'Ctrl+':'')+(event.altKey?'Alt+':'')+(event.shiftKey?'Shift+':'')+code;
  }
  function resolve(overrides){return Object.fromEntries(config.actions.map(action=>[action.id,Object.hasOwn(overrides,action.id)?overrides[action.id]:action.defaultChord]));}
  function configure(prefs){if(!config)return;config.overrides={...prefs.shortcutBindings};config.bindings=resolve(config.overrides);window.dispatchEvent(new Event('jot-shortcuts-changed'));}
  function validate(id,chord){
    const action=config.actions.find(action=>action.id===id);if(!action)return 'Unknown keyboard shortcut.';
    if(chord===null)return '';
    const parts=chord.split('+'),key=parts.pop(),ctrl=parts.includes('Ctrl'),alt=parts.includes('Alt'),shift=parts.includes('Shift');
    if(!config.keys.includes(key)||(!ctrl&&!alt&&!/^F\d+$/.test(key))||(!ctrl&&!alt&&shift))return 'Use Ctrl or Alt with a key, or a function key (F1–F11).';
    if(action.scope!=='global'&&ctrl&&alt)return 'Ctrl+Alt is reserved for AltGr typing in app shortcuts.';
    if(action.scope==='global'&&!ctrl&&!alt)return 'Windows shortcuts need Ctrl or Alt.';
    if(config.reserved.includes(chord)&&chord!==action.defaultChord||key==='Tab'&&!['next-tab','previous-tab'].includes(id))return label(chord)+' is reserved for standard text editing or Windows controls.';
    const other=config.actions.find(item=>item.id!==id&&config.bindings[item.id]===chord);
    return other?label(chord)+' is already assigned to '+other.label+(other.scope==='global'?' (Windows)':'')+'.':'';
  }
  function match(event,scope='app'){
    if(event.defaultPrevented||event.isComposing||event.ctrlKey&&event.altKey)return null;
    const chord=chordFor(event);if(!chord)return null;
    return config.actions.find(action=>action.scope===scope&&config.bindings[action.id]===chord)?.id||null;
  }
  const ready=JotBridge.request('shortcuts-config').then(value=>{config=value;window.dispatchEvent(new Event('jot-shortcuts-changed'));return config;});
  JotBridge.on(data=>{if(data.event==='preferences')configure(data.prefs);});
  return{ready,match,chordFor,validate,configure,label:action=>label(config?.bindings[action]),format:label,get config(){return config;}};
})();
