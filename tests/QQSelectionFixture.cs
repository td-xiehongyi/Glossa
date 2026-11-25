// Standalone WPF regression fixture, built as QQ.exe in a temporary directory.
using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

class QQSelectionFixture {
    [STAThread] static int Main(string[] args) {
        var type=Assembly.LoadFrom(args[0]).GetType("Selection");
        var read=type.GetMethod("ReadDirect");
        var app=new Application();
        var window=new Window{Title="划词取词测试（合成窗口）",Width=480,Height=175};
        var panel=new StackPanel();window.Content=panel;
        var message=new TextBox{Text="before selected fragment after",IsReadOnly=true,Height=48};
        var editor=new TextBox{Text="unrelated draft"};
        panel.Children.Add(message);panel.Children.Add(editor);
        int exit=1;
        window.ContentRendered+=async delegate {
            try {
                message.Focus();message.Select(7,17);editor.Focus();
                await Task.Delay(300);
                if(!editor.IsKeyboardFocused || message.SelectedText!="selected fragment")throw new Exception("Invalid fixture");
                var hwnd=new WindowInteropHelper(window).Handle;
                var point=message.PointToScreen(new Point(30,12));
                Func<string> capture=delegate{
                    var r=read.Invoke(null,read.GetParameters().Length==2?new object[]{hwnd,point}:new object[]{hwnd});
                    return r==null?null:(string)type.GetField("Text").GetValue(r);
                };
                var selected=await Task.Run(capture);
                Console.WriteLine("Message selection: "+(selected??"<empty>"));
                if(selected!="selected fragment"){exit=2;return;}
                message.Select(0,0);
                if(!string.IsNullOrEmpty(await Task.Run(capture)))throw new Exception("Read stale selection");
                Console.WriteLine("Empty selection: PASS");exit=0;
            }catch(Exception e){Console.WriteLine(e);}
            finally{app.Shutdown();}
        };
        app.Run(window);return exit;
    }
}
