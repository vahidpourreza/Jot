'use strict';
window.JotI18n = (() => {
  const english = {
    'کلیپ‌بورد موقتاً مشغول است. دوباره کپی کنید؛ یادداشت شما محفوظ است.':'The clipboard is temporarily busy. Try copying again. Your note is safe.',
    'ذخیره یادداشت‌ها و خروج کامل':'Save notes and quit the app','در حال بستن…':'Closing…',
    'گروه‌ها':'Groups','بازگشت به یادداشت‌ها':'Back to notes',
    'عمومی':'General','ظاهر':'Appearance','نوشتن':'Writing',
    'جست‌وجو در عنوان، متن یا گروه…':'Search titles, notes, or groups…','گروه یادداشت‌ها':'Note groups','همه یادداشت‌ها':'All notes','بدون گروه':'Ungrouped',
    'مشخصات یادداشت':'Note details','عنوان':'Title','گروه':'Group','خالی بگذار تا از خط اول استفاده شود':'Leave empty to use the first line',
    'مثلاً پروژه، ایده‌ها یا شخصی':'For example: Work, Ideas, or Personal','نام گروه جدید را بنویس یا یکی از گروه‌های موجود را انتخاب کن.':'Enter a new group name or choose an existing group.',
    'ذخیره':'Save','باز کردن':'Open','عنوان و گروه':'Title and group','Jot را به سلیقه خودت تنظیم کن.':'Make Jot feel like you.',
    'عنوان یا نام گروه طولانی است.':'The title or group name is too long.',
    'فضایی برای فکرهایت':'A place for your thoughts','پنهان کردن':'Hide','یادداشت‌های من':'My notes',
    'هر یادداشت، یک پنجره کنار کارت.':'A small window for every thought.','یادداشت جدید':'New note','یادداشت‌ها':'Notes',
    'ظاهر برنامه':'Appearance','جست‌وجو در یادداشت‌ها…':'Search your notes…','جست‌وجو':'Search',
    'فکرت را همین‌جا نگه دار':'Keep that thought','یک یادداشت بساز؛ پنجره کوچکش را کنار کارت بگذار.':'Create a note and keep its small window beside your work.',
    'اولین یادداشت':'Create your first note','حالت نمایش':'Theme','برای همه پنجره‌های Jot':'For all Jot windows',
    'روشن':'Light','تیره':'Dark','رنگ اصلی':'Accent color','دکمه‌ها، آیکن‌ها و حالت‌های انتخاب‌شده':'Buttons, icons, and selected states',
    'آیکن‌های رنگی':'Accent-colored icons','آیکن‌ها هماهنگ با رنگ اصلی':'Match icons to the accent color','ضخامت آیکن‌ها':'Icon weight',
    'ظریف':'Fine','معمولی':'Regular','پررنگ':'Bold','اندازه نوشته':'Text size','نوار ابزار نوشتن':'Writing toolbar',
    'همیشه در دسترس؛ قابل پنهان کردن':'Visible by default; hide it when you want','نمایش ابزار نوشتن':'Show writing tools',
    'همه‌چیز، به سلیقه تو':'Make it yours','تغییرات بلافاصله روی یادداشت‌های باز اعمال می‌شوند.':'Changes apply to all open notes immediately.',
    'ذخیره محلی · روی همین دستگاه':'Saved locally · on this device','زبان برنامه':'App language',
    'در هر دو حالت می‌توانید فارسی و English بنویسید.':'Write Persian and English in either interface.',
    'خانه یادداشت‌ها':'Notes home','همیشه روی پنجره‌های دیگر':'Keep on top','متن یادداشت · Persian and English':'Note text · Persian and English',
    'ذخیره خودکار':'Autosave','آماده':'Ready','قالب‌بندی متن':'Text formatting','قالب‌بندی':'Formatting','افزودن تصویر':'Insert image',
    'افزودن تصویر · یا چسباندن با Ctrl+V':'Insert image · or paste with Ctrl+V','تنظیمات':'Settings','سبک پاراگراف':'Paragraph style',
    'متن':'Text','قالب متن':'Text style','مورب':'Italic','زیرخط':'Underline','فهرست':'Bullet list','فهرست شماره‌دار':'Numbered list',
    'رنگ متن':'Text color','ابزارهای بیشتر':'More tools','متن معمولی':'Normal text','تیتر':'Heading','نقل‌قول':'Quote','کد':'Code',
    'رنگ نوشته':'Text color','خط‌خورده':'Strikethrough','پاک کردن قالب':'Clear formatting','روشن / تیره':'Light / dark',
    'اندازه متن':'Text size','کوچک‌تر':'Smaller','بزرگ‌تر':'Larger','خروجی یادداشت':'Export note','کپی کامل یادداشت':'Copy complete note',
    'کپی فقط متن':'Copy text only','خروج از Jot':'Quit Jot','نمایش / پنهان':'Show / hide','فارسی و English به‌صورت خودکار':'Automatic Persian / English direction',
    'بستن':'Close','تصویر':'Image','اندازه مناسب':'Fit to window','اندازه اصلی':'Original size','کپی تصویر':'Copy image','تصویر یادداشت':'Note image',
    'ذخیره نشد · Ctrl+S':'Not saved · Ctrl+S','در حال ذخیره…':'Saving…','ذخیره شد':'Saved','در حال ساخت…':'Creating…',
    'یادداشت تصویری':'Image note','یادداشت بدون متن':'Empty note','یادداشت تازه':'New note','یادداشتی پیدا نشد.':'No notes found.',
    'برای نوشتن باز کن…':'Open to start writing…','پیش‌فرض':'Default','قرمز':'Red','طلایی':'Gold','سبز':'Green','آبی':'Blue','بنفش':'Violet',
    'زرشکی':'Crimson','خنثی':'Neutral','کهربایی':'Amber','فیروزه‌ای':'Cyan','زمردی':'Emerald','سرخابی':'Fuchsia','نیلی':'Indigo',
    'لیمویی':'Lime','نارنجی':'Orange','صورتی':'Pink','ارغوانی':'Purple','رز':'Rose','آسمانی':'Sky','سبزآبی':'Teal','زرد':'Yellow',
    'عملیات انجام نشد.':'The operation could not be completed.','ارتباط با برنامه برقرار نیست.':'The connection to Jot is unavailable.',
    'پاسخی از برنامه دریافت نشد.':'Jot did not respond in time.','خطایی رخ داد؛ یادداشت شما در ویرایشگر باقی مانده است.':'Something went wrong. Your draft is still in the editor.',
    'یادداشت‌ها هنوز آماده نیستند.':'Notes are not ready yet.','فرمت تصویر پشتیبانی نمی‌شود.':'This image format is not supported.',
    'اندازه هر تصویر باید کمتر از ۸ مگابایت باشد.':'Each image must be smaller than 8 MB.','تصویر خوانده نشد.':'The image could not be read.',
    'یادداشت تغییر کرد. تصویر را دوباره بچسبانید.':'The note changed. Please paste the image again.',
    'حجم این یادداشت زیاد است؛ تصویر را در یادداشت جدید قرار دهید.':'This note is too large. Add the image to a new note.',
    'یادداشت تغییر کرد؛ دوباره بچسبانید.':'The note changed. Please paste again.',
    'بخشی از تصاویر قابل دریافت نبود؛ جای آن‌ها مشخص شده است.':'Some images could not be retrieved. Their positions are marked.',
    '[تصویر دریافت نشد]':'[Image unavailable]','یادداشت پیدا نشد.':'Note not found.','یادداشت‌های قبلی قابل خواندن نیستند.':'The previous notes could not be read.',
    'میانبر Ctrl+Alt+J در برنامه دیگری استفاده می‌شود. از آیکن کنار ساعت استفاده کنید.':'Ctrl+Alt+J is used by another app. Use the system tray icon.',
    'یادداشت معتبر نیست.':'The note is not valid.','این پنجره فقط یادداشت خودش را ذخیره می‌کند.':'This window can only save its own note.',
    'ذخیره یکی از یادداشت‌ها انجام نشد.':'One of the notes could not be saved.','تصویر معتبر نیست.':'The image is not valid.',
    'حجم یادداشت‌ها از ۶۴ مگابایت بیشتر شده است.':'Your notes exceed the 64 MB storage limit.',
    'یادداشت پیدا نشد؛ متن شما در پنجره حفظ شده است.':'The note could not be found. Your draft remains in the window.',
    'ساختار فایل یادداشت معتبر نیست. نسخه اصلی حفظ شد.':'The notes file is not valid. The original was preserved.',
    'یک یادداشت معتبر نیست. ذخیره متوقف شد.':'A note is invalid. Saving was stopped.',
    'تصویر فقط از نشانی HTTPS قابل دریافت است.':'Images can only be retrieved from HTTPS addresses.',
    'تصویر بزرگ‌تر از ۸ مگابایت است.':'The image is larger than 8 MB.'
  };
  let language='en';
  const originals=new WeakMap(),attributes=new WeakMap();
  const reverse=new Map(Object.entries(english).map(([source,translated])=>[translated,source]));
  const canonical=value=>value.replace(value.trim(),reverse.get(value.trim())||value.trim());
  const protectedText='.editor,.note-card,.note-row,#groupFilters,#groupSuggestions,#placeholder,#saveLabel,#homeStatus,#noteCount,#blockLabel,#error,#homeError,#imageError,#imageDimensions';
  function text(value) {
    if(language!=='en')return value;
    if(english[value])return english[value];
    for(const marker of [' · ',' (']){
      const index=value.indexOf(marker);
      if(index>0&&english[value.slice(0,index)])return english[value.slice(0,index)]+value.slice(index);
    }
    return value;
  }
  function apply(next) {
    language='en';
    document.documentElement.lang=language;document.documentElement.dir=language==='fa'?'rtl':'ltr';
    document.querySelectorAll('.home-app,.index-app,.settings-app,.metadata-dialog,.format-popover,.notes-dialog,#menu').forEach(node=>node.dir=language==='fa'?'rtl':'ltr');
    const walker=document.createTreeWalker(document.body,NodeFilter.SHOW_TEXT);
    let node;
    while(node=walker.nextNode()){
      if(node.parentElement?.closest(protectedText+',script,style,svg'))continue;
      if(!originals.has(node))originals.set(node,canonical(node.textContent));
      const original=originals.get(node),trimmed=original.trim();
      node.textContent=original.replace(trimmed,text(trimmed));
    }
    document.querySelectorAll('[title],[aria-label],[placeholder],[alt]').forEach(node=>{
      if(node.closest('.editor')&&node.id!=='editor')return;
      if(!attributes.has(node))attributes.set(node,{});
      const values=attributes.get(node);
      for(const attribute of ['title','aria-label','placeholder','alt']){
        if(!node.hasAttribute(attribute))continue;
        values[attribute]??=canonical(node.getAttribute(attribute));
        node.setAttribute(attribute,text(values[attribute]));
      }
    });
  }
  return {text,apply,get language(){return language;},get locale(){return language==='fa'?'fa-IR':'en-US';},dictionary:english};
})();
