using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Automation;
using System.Windows;

class Selection {
    [DllImport("user32")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32")] static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr info);
    [DllImport("user32")] static extern uint GetClipboardSequenceNumber();
    [DllImport("user32")] static extern IntPtr GetClipboardOwner();
    [DllImport("user32")] static extern bool OpenClipboard(IntPtr hwnd);
    [DllImport("user32")] static extern bool CloseClipboard();
    [DllImport("user32")] static extern uint EnumClipboardFormats(uint fmt);
    [DllImport("user32")] static extern int CountClipboardFormats();
    [DllImport("user32")] static extern bool EmptyClipboard();
    [DllImport("user32")] static extern IntPtr GetClipboardData(uint fmt);
    [DllImport("user32")] static extern IntPtr SetClipboardData(uint fmt,IntPtr data);
    [DllImport("ole32")] static extern IntPtr OleDuplicateData(IntPtr source,ushort format,uint flags);
    [DllImport("gdi32")] static extern bool DeleteObject(IntPtr handle);
    [DllImport("gdi32")] static extern bool DeleteEnhMetaFile(IntPtr handle);
    [DllImport("kernel32")] static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32")] static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32")] static extern IntPtr GlobalFree(IntPtr h);
    [DllImport("kernel32")] static extern UIntPtr GlobalSize(IntPtr h);
    public string Text; public Rect Bounds=Rect.Empty;
    public static uint Pid(IntPtr h){uint p;GetWindowThreadProcessId(h,out p);return p;}
    [DllImport("user32")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32")] static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    static bool IsPebrel(IntPtr window) {
        try {
            using(var process=System.Diagnostics.Process.GetProcessById((int)Pid(window)))
                return string.Equals(process.ProcessName,"pebrel",StringComparison.OrdinalIgnoreCase);
        }catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}
    }
    public static Selection ReadDirect(IntPtr window,Point point) {
        bool qq=false;
        try {
            using(var process=System.Diagnostics.Process.GetProcessById((int)Pid(window)))
                qq=string.Equals(process.ProcessName,"QQ",StringComparison.OrdinalIgnoreCase);
            var focused=AutomationElement.FocusedElement;
            if(focused!=null && focused.Current.IsPassword)return new Selection();
            // QQ can leave keyboard focus in the editor while text is selected in a message.
            if(qq && GetAncestor(WindowFromPoint(new System.Drawing.Point((int)point.X,(int)point.Y)),2)==window){
                try{
                    var hit=ReadPath(AutomationElement.FromPoint(point),window);
                    if(hit!=null && (hit.Text!=null || hit.BlockCopy))return hit;
                }catch(ElementNotAvailableException){}
            }
            var result=ReadPath(focused,window);
            if((qq || IsPebrel(window)) && result!=null && !result.BlockCopy && result.Text==null)return null;
            return result;
        }catch(Exception){return null;}
    }
    bool BlockCopy;
    static Selection ReadPath(AutomationElement e,IntPtr window) {
            bool emptySelection=false;
            for(int i=0;e!=null && i<8;i++,e=TreeWalker.ControlViewWalker.GetParent(e)){
                if(e.Current.ProcessId!=Pid(window))break; if(e.Current.IsPassword)return new Selection{BlockCopy=true};
                object pattern;
                if(!e.TryGetCurrentPattern(TextPattern.Pattern,out pattern))continue;
                var ranges=((TextPattern)pattern).GetSelection();
                emptySelection=true; if(ranges.Length==0)continue;
                var selected=string.Join("\n",ranges.Select(r=>r.GetText(4001))).Trim();
                if(selected.Length==0)continue;
                var result=new Selection { Text=selected };
                try{
                    var rects=ranges[0].GetBoundingRectangles();
                    if(rects.Length>0)result.Bounds=rects[rects.Length-1];
                }catch(InvalidOperationException){}catch(NotSupportedException){}
                return result;
            }
            if(emptySelection)return new Selection();
        return null;
    }
    static string ClipboardText(){
        var h=GetClipboardData(13);
        if(h==IntPtr.Zero || GlobalSize(h).ToUInt64()>8*1024*1024)return null;
        var p=GlobalLock(h);if(p==IntPtr.Zero)return null;
        try{return Marshal.PtrToStringUni(p);}finally{GlobalUnlock(h);}
    }
    sealed class ClipboardBackup : IDisposable {
        sealed class Entry { public uint Format; public IntPtr Handle; }
        readonly List<Entry> entries=new List<Entry>();

        // Caller holds the clipboard open throughout capture or restore.
        public void Capture(){
            long total=0;
            for(uint f=EnumClipboardFormats(0);f!=0;f=EnumClipboardFormats(f)){
                bool bitmap=f==2 || f==9 || f==0x82;
                bool enhanced=f==14 || f==0x8e;
                // Private handles, owner-display and metafile pictures need producer-specific cleanup.
                if(!bitmap && !enhanced && !(f==1 || f>=4 && f<=17 && f!=14 || f==0x81 || f>=0xc000))
                    throw new InvalidOperationException("剪贴板含有无法备份的专用格式，已保留原内容。");
                var data=GetClipboardData(f);
                if(data==IntPtr.Zero)throw new InvalidOperationException("原剪贴板数据暂不可读，请重试。");
                if(!bitmap && !enhanced){
                    var size=GlobalSize(data).ToUInt64();
                    if(size==0 || size>(ulong)(67108864-total))
                        throw new InvalidOperationException("原剪贴板数据过大或无法备份，已保留原内容。");
                    total+=(long)size;
                }
                var copy=OleDuplicateData(data,(ushort)f,2);
                if(copy==IntPtr.Zero)throw new InvalidOperationException("无法备份原剪贴板，请重试。");
                entries.Add(new Entry{Format=f,Handle=copy});
            }
        }
        public void Restore(){
            if(!EmptyClipboard())throw new InvalidOperationException("无法恢复原剪贴板。");
            bool failed=false;
            foreach(var entry in entries){
                if(SetClipboardData(entry.Format,entry.Handle)!=IntPtr.Zero)entry.Handle=IntPtr.Zero;
                else failed=true;
            }
            if(failed)throw new InvalidOperationException("部分原剪贴板格式未能恢复。");
        }
        public void Dispose(){
            foreach(var entry in entries){
                if(entry.Handle==IntPtr.Zero)continue;
                if(entry.Format==2 || entry.Format==9 || entry.Format==0x82)DeleteObject(entry.Handle);
                else if(entry.Format==14 || entry.Format==0x8e)DeleteEnhMetaFile(entry.Handle);
                else GlobalFree(entry.Handle);
            }
        }
    }
    public static async Task<Selection> CopyFallback(IntPtr original,IntPtr app,Func<int> activity,CancellationToken token){
        for(int i=0;i<50 && new[]{16,17,18,91,92}.Any(k=>GetAsyncKeyState(k)<0);i++) await Task.Delay(10,token);
        token.ThrowIfCancellationRequested();
        if(original!=GetForegroundWindow() || new[]{16,17,18,91,92}.Any(k=>GetAsyncKeyState(k)<0))return null;
        bool pebrel=IsPebrel(original);
        int epoch=activity(); uint before;
        using(var backup=new ClipboardBackup()){
            if(!OpenClipboard(app))return null;
            try{
                backup.Capture();
                before=GetClipboardSequenceNumber();
            }finally{CloseClipboard();}
            token.ThrowIfCancellationRequested();
            if(activity()!=epoch)return null;
            if(before!=GetClipboardSequenceNumber() || original!=GetForegroundWindow())return null;
            keybd_event(17,0,0,UIntPtr.Zero);
            if(pebrel)keybd_event(16,0,0,UIntPtr.Zero);
            keybd_event(67,0,0,UIntPtr.Zero);
            keybd_event(67,0,2,UIntPtr.Zero);
            if(pebrel)keybd_event(16,0,2,UIntPtr.Zero);
            keybd_event(17,0,2,UIntPtr.Zero);
            // Finish the clipboard transaction even if the translation request is cancelled.
            for(int i=0;i<35;i++){
                await Task.Delay(12);
                if(activity()!=epoch || original!=GetForegroundWindow())return null;
                if(GetClipboardSequenceNumber()==before || !OpenClipboard(app))continue;
                try{
                    var owner=GetClipboardOwner();
                    // GPUI writes terminal selections with OpenClipboard(None).
                    if(owner==IntPtr.Zero ? !pebrel : Pid(owner)!=Pid(original))return null;
                    var s=ClipboardText();
                    if(string.IsNullOrWhiteSpace(s))return null;
                    if(owner==IntPtr.Zero){
                        // Pebrel writes only plain text. Leave unrelated rich updates untouched.
                        for(uint f=EnumClipboardFormats(0);f!=0;f=EnumClipboardFormats(f))
                            if(f!=1 && f!=7 && f!=13 && f!=16)return null;
                    }
                    backup.Restore();
                    return new Selection{Text=s.Trim()};
                }finally{CloseClipboard();}
            }
            return null;
        }
    }
}

