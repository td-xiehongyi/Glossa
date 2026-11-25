using System;
using System.Drawing;
using System.Drawing.Text;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal sealed class TrayMenu : ContextMenuStrip {
    readonly Font menuFont;
    readonly Font iconFont;
    readonly int iconSize;

    internal TrayMenu(Action settings, Action exit) {
        float scale;
        using(var g=CreateGraphics())scale=g.DpiX/96f;
        Func<int,int> px=n=>(int)Math.Round(n*scale);
        menuFont=new Font("Microsoft YaHei UI",10f);
        Font=menuFont;
        iconSize=px(20);
        iconFont=new Font("Segoe MDL2 Assets",px(16),FontStyle.Regular,GraphicsUnit.Pixel);
        ImageScalingSize=new Size(iconSize,iconSize);
        Padding=new Padding(px(4),px(5),px(4),px(5));
        if(SystemInformation.HighContrast){
            RenderMode=ToolStripRenderMode.System;
        }else{
            BackColor=Color.FromArgb(248,251,253);
            ForeColor=Color.FromArgb(31,54,72);
            Renderer=new MenuRenderer(scale);
        }
        MinimumSize=new Size(px(148),0);
        AddAction("设置",'',settings,scale);
        AddAction("退出",'\uE7E8',exit,scale);
    }

    void AddAction(string label,char glyph,Action action,float scale){
        var icon=new Bitmap(iconSize,iconSize);
        using(var g=Graphics.FromImage(icon))
        using(var brush=new SolidBrush(SystemInformation.HighContrast?SystemColors.MenuText:Color.FromArgb(56,93,119)))
        using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}){
            g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
            g.DrawString(glyph.ToString(),iconFont,brush,new RectangleF(0,0,iconSize,iconSize),format);
        }
        var item=new ToolStripMenuItem(label,icon,(s,e)=>action()){
            AutoSize=false,Size=new Size(MinimumSize.Width-2,(int)Math.Round(35*scale))
        };
        item.Disposed+=(s,e)=>icon.Dispose();
        Items.Add(item);
    }

    protected override void Dispose(bool disposing){
        base.Dispose(disposing);
        if(disposing){menuFont.Dispose();iconFont.Dispose();}
    }

    sealed class MenuRenderer : ToolStripProfessionalRenderer {
        readonly int iconLeft, textLeft;
        internal MenuRenderer(float scale) : base(new MenuColors()) {
            iconLeft=(int)Math.Round(12*scale);
            textLeft=iconLeft+(int)Math.Round(30*scale);
        }
        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e){
            if(e.Image==null)return;
            var size=e.ToolStrip.ImageScalingSize;
            e.Graphics.DrawImage(e.Image,new Rectangle(iconLeft,(e.Item.Height-size.Height)/2,size.Width,size.Height));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){
            e.TextRectangle=new Rectangle(textLeft,0,e.Item.Width-textLeft,e.Item.Height-2);
            e.TextFormat=TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){
            if(!e.Item.Selected)return;
            var bounds=new Rectangle(3,2,Math.Min(e.Item.Width,e.ToolStrip.ClientSize.Width-e.Item.Bounds.Left)-6,e.Item.Height-4);
            int d=Math.Min(12,bounds.Height);
            using(var path=new GraphicsPath())
            using(var fill=new SolidBrush(ColorTable.MenuItemSelected)){
                path.AddArc(bounds.Left,bounds.Top,d,d,180,90);
                path.AddArc(bounds.Right-d,bounds.Top,d,d,270,90);
                path.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);
                path.AddArc(bounds.Left,bounds.Bottom-d,d,d,90,90);
                path.CloseFigure();
                var state=e.Graphics.Save();
                e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                e.Graphics.FillPath(fill,path);
                e.Graphics.Restore(state);
            }
        }
    }

    sealed class MenuColors : ProfessionalColorTable {
        static readonly Color paper=Color.FromArgb(248,251,253);
        public override Color ToolStripDropDownBackground {get{return paper;}}
        public override Color ImageMarginGradientBegin {get{return paper;}}
        public override Color ImageMarginGradientMiddle {get{return paper;}}
        public override Color ImageMarginGradientEnd {get{return paper;}}
        public override Color MenuItemSelected {get{return Color.FromArgb(222,237,247);}}
        public override Color MenuItemBorder {get{return Color.FromArgb(191,215,231);}}
        public override Color MenuBorder {get{return Color.FromArgb(191,209,221);}}
        public override Color SeparatorDark {get{return Color.FromArgb(209,223,232);}}
        public override Color SeparatorLight {get{return paper;}}
    }
}
