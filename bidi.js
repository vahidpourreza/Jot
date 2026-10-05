'use strict';
window.JotBidi=(()=>{
  const scopeSelector='p,div,h1,h2,h3,h4,h5,h6,li,blockquote,table,td,th,pre';
  const marker='bdi[data-jot-bidi="token"]';
  const atomSelector='img,br';
  // Technical units and Latin runs inside RTL prose are isolated, never
  // reordered. Ordinary prose still follows Unicode first-strong direction.
  const digit='0-9۰-۹٠-٩';
  const tokenPattern=new RegExp([
    String.raw`(?:https?:\/\/|ftp:\/\/|www\.)[^\s<>"'\uFFFC]+`,
    String.raw`(?<![A-Za-z0-9.!#$%&'*+\/=?^_\x60{|}~-])[A-Za-z0-9.!#$%&'*+\/=?^_\x60{|}~-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}`,
    String.raw`["'][A-Za-z]:[\\/][^"'\r\n\uFFFC]+["']`,
    String.raw`["'](?:~?\/|\.\.?\/)[^"'\r\n\uFFFC]+["']`,
    String.raw`(?:[A-Za-z]:[\\/]|\\\\)[^\s<>"'\uFFFC]+`,
    String.raw`(?:~?\/|\.\.?\/)[\p{L}\p{N}_.~-]+(?:\/[\p{L}\p{N}_.~-]+)*\/?(?:\?[^\s<>"'\uFFFC]*)?`,
    String.raw`\x60[^\x60\r\n\uFFFC]+\x60`,
    String.raw`(?<![\p{L}\p{N}_-])--?[A-Za-z][\w-]*(?:=[^\s<>"'\uFFFC]+)?`,
    String.raw`\b[A-Za-z_$][\w.$]*<[A-Za-z0-9_$.,? \t\[\]<>]+>(?:\[\])*`,
    String.raw`\b[A-Za-z_$][\w.$]*(?:\[\])+`,
    String.raw`\bC(?:\+\+|#)|\.NET\b(?:[ \t]+\d+(?:\.\d+)*)?`,
    String.raw`\b[A-Za-z_$][\w.$]*\([^()\r\n\uFFFC]*\)`,
    String.raw`\b[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)+\b`,
    String.raw`\((?=[^()\r\n]*[A-Za-z])[^()\r\n\u0600-\u06ff\uFFFC]+\)`,
    String.raw`\b[A-Za-z0-9_.~-]+(?:[/\\][A-Za-z0-9_.~-]+)+`,
    String.raw`\b[A-Za-z_][\w.-]*\.(?:tsx?|jsx?|cs|json|html?|css|md|py|txt|exe|dll|xml|ya?ml|sql|png|jpe?g|pdf)\b`,
    String.raw`\bv?\d+(?:\.\d+)+(?:[-+][A-Za-z0-9_.-]+)?`,
    `(?:\\+[${digit}]{1,4}[ -]*)?\\([${digit}]{2,4}\\)[ -]*[${digit}]{2,8}(?:[ -][${digit}]{2,8})*`,
    `[+−-]?[${digit}]+(?:[.,٫٬:/-][${digit}]+)*(?:[ \u00a0][${digit}]{2,})*(?:[ \u00a0]?[٪%])?`
  ].join('|'),'giu');
  const rtlLetter=/[\p{Script_Extensions=Arabic}\p{Script=Hebrew}]/u;
  const letter=/\p{L}/u;
  const englishProse=new Set('the a an is are was were use using add remove set please this that with without for to from in on and or of we you it as text word label hello'.split(' '));
  const codeWords=new Set('enum class interface struct record const var int string bool async await public private static void namespace'.split(' '));
  function firstDirection(text,fallback){
    for(const char of text){if(char==='\u200f')return 'rtl';if(char==='\u200e')return 'ltr';if(letter.test(char))return rtlLetter.test(char)?'rtl':'ltr';}return fallback;
  }
  function technicalTokens(text){
    if(!/[.:/\\@()\[\]<>`#%٪+−\-0-9۰-۹٠-٩]/.test(text))return [];
    const matches=[];tokenPattern.lastIndex=0;
    for(const match of text.matchAll(tokenPattern)){
      let value=match[0];
      if(/^(?:https?:\/\/|ftp:\/\/|www\.|[A-Za-z]:[\\/]|\\\\|~?\/|\.\.?\/|--?)/i.test(value)){
        value=value.replace(/[.,;:!?،؛؟…»”’›]+$/u,'');
        for(const [open,close] of [['(',')'],['[',']'],['{','}']])
          while(value.endsWith(close)&&value.split(close).length>value.split(open).length)value=value.slice(0,-1);
      }
      if(!value)continue;
      // Do not isolate the middle of ordinary identifiers such as abc123def.
      if(new RegExp(`^[+−-]?[${digit}]`).test(value)){
        const before=text[match.index-1]||'',after=text[match.index+value.length]||'';
        if(/[A-Za-z_]/.test(before)||/[A-Za-z_]/.test(after))continue;
      }
      matches.push({start:match.index,end:match.index+value.length,text:value});
    }
    return matches;
  }
  function paragraphDirection(text,keyboard){
    const technical=technicalTokens(text);let natural='',offset=0;
    for(const token of technical){natural+=text.slice(offset,token.start)+' ';offset=token.end;}natural+=text.slice(offset);
    const first=firstDirection(natural,null);
    if(first!=='ltr')return first||firstDirection(text,keyboard);
    const rtlIndex=natural.search(/[\p{Script_Extensions=Arabic}\p{Script=Hebrew}]/u);
    if(rtlIndex<0)return 'ltr';
    const prefix=natural.slice(0,rtlIndex).replace(/^[\s\d۰-۹٠-٩.()\[\]•*-]+/u,'').trim();
    const words=prefix.match(/[A-Za-z_$][\w$]*/g)||[];
    const identifiersOnly=words.length>0&&words.length<=4&&words.every(word=>!englishProse.has(word.toLowerCase())&&(codeWords.has(word)||/[A-Z_$]/.test(word)));
    if(!identifiersOnly)return 'ltr';
    const suffix=natural.slice(rtlIndex),rtlWords=(suffix.match(/[\p{Script_Extensions=Arabic}\p{Script=Hebrew}\u200c\u200d]+/gu)||[]).filter(word=>letter.test(word));
    const laterEnglish=suffix.match(/[A-Za-z_$][\w$]*/g)||[];
    const noLaterProse=laterEnglish.every(word=>!englishProse.has(word.toLowerCase())&&(/[A-Z_$]/.test(word)||codeWords.has(word)));
    // Conservative exception for technical changelog sentences, e.g.
    // "Currency enum حذف شد.". Ordinary English prose keeps first-strong LTR.
    return identifiersOnly&&rtlWords.length>=2&&noLaterProse?'rtl':'ltr';
  }
  function tokens(text,direction){
    const matches=technicalTokens(text);
    if(rtlLetter.test(text)&&(direction||paragraphDirection(text,'ltr'))==='rtl'){
      for(const match of text.matchAll(/[A-Za-z_$][\w$]*(?:[.-][\w$]+)*(?:[ \t]+[A-Za-z_$][\w$]*(?:[.-][\w$]+)*)*/g)){
        const start=match.index,end=start+match[0].length;
        if(!matches.some(hit=>start<hit.end&&end>hit.start))matches.push({start,end,text:match[0]});
      }
    }
    matches.sort((a,b)=>a.start-b.start||b.end-a.end);
    const merged=[];
    for(const token of matches){
      const previous=merged.at(-1);
      if(previous&&token.start>=previous.end&&/^[ \t]*(?:[,:=+*/<>-][ \t]*)?$/.test(text.slice(previous.end,token.start))){previous.end=token.end;previous.text=text.slice(previous.start,previous.end);}
      else merged.push({...token});
    }
    return merged;
  }
  function units(root){
    const items=[];let length=0;
    function walk(node){
      if(node.nodeType===Node.TEXT_NODE){items.push({node,start:length,end:length+node.length});length+=node.length;}
      else if(node.nodeType===Node.ELEMENT_NODE&&node.matches(atomSelector)){items.push({node,start:length,end:++length});}
      else for(const child of node.childNodes)walk(child);
    }
    walk(root);return {items,length};
  }
  function pointOffset(root,node,offset){
    let total=0,answer=null;
    function size(item){if(item.nodeType===Node.TEXT_NODE)return item.length;if(item.nodeType===Node.ELEMENT_NODE&&item.matches(atomSelector))return 1;return [...item.childNodes].reduce((sum,child)=>sum+size(child),0);}
    function walk(item){
      if(answer!==null)return;
      if(item===node){answer=total+(item.nodeType===Node.TEXT_NODE?offset:[...item.childNodes].slice(0,offset).reduce((sum,child)=>sum+size(child),0));return;}
      if(item.nodeType===Node.TEXT_NODE)total+=item.length;
      else if(item.nodeType===Node.ELEMENT_NODE&&item.matches(atomSelector))total++;
      else for(const child of item.childNodes)walk(child);
    }
    walk(root);return answer;
  }
  function pointAt(root,offset){
    const {items,length}=units(root);offset=Math.max(0,Math.min(length,offset));
    for(const item of items){
      if(offset>item.end)continue;
      if(item.node.nodeType===Node.TEXT_NODE)return [item.node,Math.min(item.node.length,offset-item.start)];
      const index=[...item.node.parentNode.childNodes].indexOf(item.node);
      return [item.node.parentNode,index+(offset>item.start?1:0)];
    }
    return [root,root.childNodes.length];
  }
  function snapshot(root,bookmark){
    function endpoint(node,offset){
      const element=node.nodeType===Node.ELEMENT_NODE?node:node.parentElement;
      const nearest=element?.closest(scopeSelector),scope=nearest&&root.contains(nearest)?nearest:root;
      return {scope,offset:pointOffset(scope,node,offset),node,originalOffset:offset,text:node.nodeType===Node.TEXT_NODE?node.data:null,
        before:node===root?node.childNodes[offset]:null,after:node===root&&offset?node.childNodes[offset-1]:null};
    }
    const selection=getSelection();
    const selected=selection.rangeCount&&root.contains(selection.anchorNode)&&root.contains(selection.focusNode)
      ?[endpoint(selection.anchorNode,selection.anchorOffset),endpoint(selection.focusNode,selection.focusOffset)]:null;
    const saved=bookmark&&root.contains(bookmark.startContainer)&&root.contains(bookmark.endContainer)
      ?[endpoint(bookmark.startContainer,bookmark.startOffset),endpoint(bookmark.endContainer,bookmark.endOffset)]:null;
    return {selected,saved};
  }
  function restore(root,state,bookmark){
    function locate(point){
      if(point.text!==null&&root.contains(point.node)&&point.node.data===point.text)return [point.node,point.originalOffset];
      if(point.before&&point.before.parentNode===root)return [root,[...root.childNodes].indexOf(point.before)];
      if(point.after&&point.after.parentNode===root)return [root,[...root.childNodes].indexOf(point.after)+1];
      return pointAt(root.contains(point.scope)?point.scope:root,point.offset);
    }
    if(state.selected){
      const start=locate(state.selected[0]),end=locate(state.selected[1]),selection=getSelection();
      if(selection.anchorNode!==start[0]||selection.anchorOffset!==start[1]||selection.focusNode!==end[0]||selection.focusOffset!==end[1])selection.setBaseAndExtent(...start,...end);
    }
    if(state.saved){const range=document.createRange();range.setStart(...locate(state.saved[0]));range.setEnd(...locate(state.saved[1]));return range;}
    return bookmark;
  }
  function wrapLooseContent(root,bookmark){
    const rootBlocks='p,div,h1,h2,h3,h4,h5,h6,ul,ol,blockquote,table,pre,figure,hr';
    if(root.children.length&&[...root.childNodes].every(node=>node.nodeType===Node.ELEMENT_NODE&&node.matches(rootBlocks)||node.nodeType===Node.TEXT_NODE&&!node.data.trim()))return bookmark;
    const state=snapshot(root,bookmark);let paragraph=null;
    for(const node of [...root.childNodes]){
      if(node.nodeType===Node.ELEMENT_NODE&&node.matches(rootBlocks)){paragraph=null;continue;}
      if(!paragraph){paragraph=document.createElement('p');paragraph.dir='auto';root.insertBefore(paragraph,node);}
      paragraph.append(node);
    }
    if(!root.hasChildNodes()){paragraph=document.createElement('p');paragraph.dir='auto';paragraph.append(document.createElement('br'));root.append(paragraph);}
    return restore(root,state,bookmark);
  }
  function content(block){
    const segments=[],wrappers=[];let text='';
    function walk(node){
      if(node.nodeType===Node.TEXT_NODE){segments.push({node,start:text.length,end:text.length+node.length});text+=node.data;return;}
      if(node.nodeType!==Node.ELEMENT_NODE)return;
      if(node!==block&&(node.matches(scopeSelector)||node.matches('code,bdi:not([data-jot-bidi])'))){text+='\uFFFC';return;}
      if(node.matches(atomSelector)){text+=node.tagName==='BR'?'\n':'\uFFFC';return;}
      const wrapper=node.matches(marker)?{node,start:text.length,end:0}:null;
      for(const child of node.childNodes)walk(child);
      if(wrapper){wrapper.end=text.length;wrappers.push(wrapper);}
    }
    walk(block);return {text,segments,wrappers};
  }
  function plan(block){
    if(block.closest('pre,code'))return null;
    const data=content(block),matches=tokens(data.text,block.dataset.jotDirection),kept=new Set(),remove=[];
    const byBounds=new Map(matches.map(match=>[match.start+':'+match.end,match]));
    for(const {node:wrapper,start,end} of data.wrappers){
      const match=byBounds.get(start+':'+end);
      if(match&&wrapper.dir==='ltr'&&!wrapper.querySelector(marker))kept.add(match);else remove.push(wrapper);
    }
    return {block,remove,add:matches.filter(hit=>!kept.has(hit))};
  }
  function apply(plan){
    for(const wrapper of plan.remove)wrapper.replaceWith(...wrapper.childNodes);
    const {text,segments}=content(plan.block);
    if(segments.length===1&&segments[0].node.data===text&&plan.add.length){
      // A large paste can contain thousands of runs. Build once off-DOM rather
      // than repeatedly splitting the same long text node with live Ranges.
      const fragment=document.createDocumentFragment();let offset=0;
      for(const token of plan.add){
        if(token.start>offset)fragment.append(document.createTextNode(text.slice(offset,token.start)));
        const wrapper=document.createElement('bdi');wrapper.dir='ltr';wrapper.dataset.jotBidi='token';wrapper.textContent=text.slice(token.start,token.end);fragment.append(wrapper);offset=token.end;
      }
      if(offset<text.length)fragment.append(document.createTextNode(text.slice(offset)));
      segments[0].node.replaceWith(fragment);return;
    }
    function point(offset,end){
      const segment=segments.find(part=>end?offset>part.start&&offset<=part.end:offset>=part.start&&offset<part.end);
      return segment?[segment.node,offset-segment.start]:null;
    }
    for(const token of plan.add.reverse()){
      const start=point(token.start,false),end=point(token.end,true);if(!start||!end)continue;
      const range=document.createRange();range.setStart(...start);range.setEnd(...end);
      const wrapper=document.createElement('bdi');wrapper.dir='ltr';wrapper.dataset.jotBidi='token';
      wrapper.append(range.extractContents());range.insertNode(wrapper);
    }
  }
  function normalize(root,blocks,keyboard,bookmark=null){
    const plans=[...blocks].filter(block=>root.contains(block)&&!block.closest('pre,code')).map(plan).filter(Boolean);
    const changed=plans.some(item=>item.remove.length||item.add.length);
    const state=changed?snapshot(root,bookmark):null;
    for(const item of plans)if(item.remove.length||item.add.length)apply(item);
    for(const block of blocks){
      if(!root.contains(block))continue;
      if(block.closest('pre,code')){block.dir='ltr';continue;}
      const manual=block.dataset.jotDirection;
      const empty=!block.textContent.replace(/[\u200b\u200c\u200d\ufeff]/g,'').trim()&&!block.querySelector('img');
      let dir=empty?keyboard:paragraphDirection(block.textContent,keyboard);
      if(!empty&&firstDirection(block.textContent,null)===null){
        if(!['ltr','rtl'].includes(block.dataset.jotNeutralDirection))block.dataset.jotNeutralDirection=['ltr','rtl'].includes(block.dir)?block.dir:keyboard;
        dir=block.dataset.jotNeutralDirection;
      }else if(block.dataset.jotNeutralDirection)delete block.dataset.jotNeutralDirection;
      if(manual==='ltr'||manual==='rtl')dir=manual;
      if(block.dir!==dir)block.dir=dir;
      if(block.dataset.emptyBlock!==String(empty))block.dataset.emptyBlock=String(empty);
    }
    return state?restore(root,state,bookmark):bookmark;
  }
  return {normalize,wrapLooseContent,tokens,paragraphDirection,pointOffset,pointAt,scopeSelector};
})();
