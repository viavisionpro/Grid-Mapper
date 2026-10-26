using GridMapper.Core;
using SkiaSharp;

namespace GridMapper.Rendering;

public static partial class SceneRenderer
{
    private static void DrawProjection(SKCanvas canvas, ProjectionSettings p, RenderWork? work)
    {
        canvas.Save(); canvas.ClipRect(new(0,0,p.WidthPx,p.HeightPx));
        try
        {
            var cell = (double)p.CellSizePx; var cx = p.WidthPx / 2d; var cy = p.HeightPx / 2d;
            var left = -(int)Math.Ceiling(cx/cell); var right = (int)Math.Ceiling(cx/cell);
            var top = -(int)Math.Ceiling(cy/cell); var bottom = (int)Math.Ceiling(cy/cell);
            using var paint = new SKPaint { IsAntialias = true, Color = ProjectionColor(p.GridColor) };
            var line = Math.Max(1, Math.Min(p.WidthPx,p.HeightPx)/1080f);
            using var font = new SKFont(EmbeddedTypefaces.Get(EmbeddedFont.NotoSans), (float)Math.Clamp(cell*.2,2,128)) { Edging = SKFontEdging.Antialias };
            if(p.GridEnabled)
            {
            for (var row=top;row<bottom;row++)
                for(var col=left;col<right;col++)
                {
                    work?.Check();
                    var x=(float)(cx+col*cell);var y=(float)(cy+row*cell);
                    var filled=p.Checkerboard && ((col+row)&1)==0;
                    paint.Style=SKPaintStyle.Fill;paint.Color=ProjectionColor(p.GridColor);
                    if(filled) canvas.DrawRect(x,y,(float)cell,(float)cell,paint);
                    if(!p.ShowIds)continue;
                    var label=ProjectionRow(row-top)+(col-left+1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var x0=Math.Max(0,x);var y0=Math.Max(0,y);var x1=Math.Min(p.WidthPx,x+(float)cell);var y1=Math.Min(p.HeightPx,y+(float)cell);
                    if (x1-x0 < font.Size || y1-y0 < font.Size) continue;
                    var tx=(x0+x1-font.MeasureText(label))/2;var ty=(y0+y1-font.Metrics.Ascent-font.Metrics.Descent)/2;
                    paint.Style=SKPaintStyle.Stroke;paint.StrokeWidth=Math.Max(.5f,font.Size*.06f);paint.Color=p.GridColor.R*.2126+p.GridColor.G*.7152+p.GridColor.B*.0722>140 ? SKColors.Black : SKColors.White;
                    if(!filled)canvas.DrawText(label,tx,ty,font,paint);
                    paint.Style=SKPaintStyle.Fill;
                    paint.Color=filled ? (p.GridColor.R*.2126+p.GridColor.G*.7152+p.GridColor.B*.0722>140 ? SKColors.Black:SKColors.White) : ProjectionColor(p.GridColor);
                    canvas.DrawText(label,tx,ty,font,paint);
                }
            paint.Color=ProjectionColor(p.GridColor);paint.Style=SKPaintStyle.Stroke;paint.StrokeWidth=line;
            for(var col=left;col<=right;col++){var x=(float)(cx+col*cell);canvas.DrawLine(x,0,x,p.HeightPx,paint);}
            for(var row=top;row<=bottom;row++){var y=(float)(cy+row*cell);canvas.DrawLine(0,y,p.WidthPx,y,paint);}
            }
            if(p.Circles)
            {
                var d=(double)p.HeightPx/p.CircleRows;var radius=d/2;
                var columns=(int)Math.Ceiling((cx+radius)/d);
                paint.Color=ProjectionColor(p.CircleColor);paint.Style=SKPaintStyle.Stroke;paint.StrokeWidth=line;
                for(var row=0;row<p.CircleRows;row++) for(var col=-columns;col<=columns;col++)
                { work?.Check();canvas.DrawCircle((float)(cx+col*d),(float)((row+.5)*d),(float)radius,paint); }
            }
            if(p.FrameEnabled) DrawProjectionFrame(canvas,p,paint,line,work);
            if(p.ShowInformation)
            {
                var lines=new[]{ $"{ProjectionSettings.Meters(p.WidthMeters)} × {ProjectionSettings.Meters(p.HeightMeters)} m", $"{p.WidthPx} \u00d7 {p.HeightPx} px",
                    $"Grid {ProjectionSettings.Meters(p.GridMeters)} m · Circle {ProjectionSettings.Meters(p.CircleDiameterMeters)} m" };
                using var info=new SKFont(EmbeddedTypefaces.Get(EmbeddedFont.NotoSansMono),Math.Clamp(Math.Min(p.WidthPx,p.HeightPx)*.025f,2,48)){Edging=SKFontEdging.Antialias};
                var maxWidth=lines.Max(s=>info.MeasureText(s));var limit=p.WidthPx*.85f;
                if(maxWidth>limit){info.Size*=limit/maxWidth;maxWidth=lines.Max(s=>info.MeasureText(s));}
                var pad=info.Size*.6f;var step=info.Size*1.5f;var width=maxWidth+pad*2;var height=step*lines.Length+pad*2;
                paint.Style=SKPaintStyle.Fill;paint.Color=new(0,0,0,204);
                canvas.DrawRect((p.WidthPx-width)/2,(p.HeightPx-height)/2,width,height,paint);
                paint.Color=SKColors.White;
                for(var i=0;i<lines.Length;i++) canvas.DrawText(lines[i],(p.WidthPx-info.MeasureText(lines[i]))/2,(p.HeightPx-height)/2+pad-info.Metrics.Ascent+i*step,info,paint);
            }
        }
        finally {canvas.Restore();}
    }
    private static void DrawProjectionFrame(SKCanvas canvas, ProjectionSettings p, SKPaint paint, float line, RenderWork? work)
    {
        paint.Color=ProjectionColor(p.FrameColor);paint.Style=SKPaintStyle.Stroke;paint.StrokeWidth=line;
        var edge=line/2;var right=p.WidthPx-edge;var bottom=p.HeightPx-edge;
        canvas.DrawRect(edge,edge,p.WidthPx-line,p.HeightPx-line,paint);
        var major=Math.Max(8,Math.Min(p.WidthPx,p.HeightPx)*.02f);var minor=major*.5f;
        var pitch=(double)p.WidthPx/(double)p.WidthMeters/10;
        var nx=(int)decimal.Floor(p.WidthMeters*5);var ny=(int)decimal.Floor(p.HeightMeters*5);
        for(var i=-nx;i<=nx;i++)
        {
            work?.Check();var x=(float)(p.WidthPx/2d+i*pitch);var length=i%10==0?major:minor;
            canvas.DrawLine(x,edge,x,Math.Min(bottom,edge+length),paint);
            canvas.DrawLine(x,bottom,x,Math.Max(edge,bottom-length),paint);
        }
        for(var i=-ny;i<=ny;i++)
        {
            work?.Check();var y=(float)(p.HeightPx/2d+i*pitch);var length=i%10==0?major:minor;
            canvas.DrawLine(edge,y,Math.Min(right,edge+length),y,paint);
            canvas.DrawLine(right,y,Math.Max(edge,right-length),y,paint);
        }
    }
    private static SKColor ProjectionColor(RgbaColor c) => new(c.R,c.G,c.B,c.A);
    private static string ProjectionRow(int row)
    {
        var result="";for(var n=row+1;n>0;n=(n-1)/26)result=(char)('A'+(n-1)%26)+result;return result;
    }
}
