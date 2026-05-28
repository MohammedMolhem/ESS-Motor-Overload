using System.Drawing;
using System.Windows.Forms;

public class MyBar : Control
{
    public Color LowProgressColor { get; set; } = Color.Red;
    public Color MediumProgressColor { get; set; } = Color.Orange;
    public Color HighProgressColor { get; set; } = Color.Yellow;
    public Color CompleteColor { get; set; } = Color.Green;

    public int Minimum { get; set; } = 0;
    public int Maximum { get; set; } = 100;

    private int _value = 0;
    public int Value
    {
        get => _value;
        set
        {
            if (value < Minimum) _value = Minimum;
            else if (value > Maximum) _value = Maximum;
            else _value = value;

            this.Invalidate(); // Redraw the control when the value changes
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (Graphics g = e.Graphics)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Calculate progress width
            int progressWidth = (int)((float)(this.Width - 4) * (_value - Minimum) / (Maximum - Minimum));

            // Determine color based on the value
            Color progressColor = LowProgressColor;
            if (_value > 66) progressColor = CompleteColor;
            else if (_value > 33) progressColor = HighProgressColor;
            else progressColor = MediumProgressColor;

            // Draw the progress bar background
            using (Brush backgroundBrush = new SolidBrush(Color.LightGray))
            {
                g.FillRectangle(backgroundBrush, 2, 2, this.Width - 4, this.Height - 4);
            }

            // Draw the progress bar
            using (Brush progressBrush = new SolidBrush(progressColor))
            {
                g.FillRectangle(progressBrush, 2, 2, progressWidth, this.Height - 4);
            }

            // Draw the border
            using (Pen borderPen = new Pen(Color.Black, 1))
            {
                g.DrawRectangle(borderPen, 1, 1, this.Width - 2, this.Height - 2);
            }
        }
    }
}