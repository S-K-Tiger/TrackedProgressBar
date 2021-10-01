using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace TrackedProgressBar {
    /// <summary>
    /// An alternative to <see cref="System.Windows.Forms.ProgressBar"/>.
    /// </summary>
    [DefaultBindingProperty(nameof(Value))]
    public partial class TrackedProgressBar : UserControl {
        #region private fields
        private readonly bool initializationComplete = false;
        private readonly BufferedGraphicsContext backBufferContext;
        private readonly BufferedGraphics[] backBufferGraphic;
        private readonly byte Layers = 5;
        private enum LayerNames { backgroundLayer, foregroundLayer, trackLayer, textLayer, draggerLayer }
        /*
            backBufferGraphic[0].Graphics = backgroundLayer;
            backBufferGraphic[1].Graphics = foregroundLayer;
            backBufferGraphic[2].Graphics = trackLayer;
            backBufferGraphic[3].Graphics = textLayer;
            backBufferGraphic[4].Graphics = draggerLayer;
        */

        private ushort _TrackStepMinor = 2;
        private ushort _TrackStepMayor = 10;
        private bool isDisposing = false;
        private bool isDragging = false;
        private bool showDragger = false;
        private bool _AllowUserDragging = false;
        private int _Maximum = 100;
        private int _Minimum = 0;
        private int _Value = 0;
        private int dragValue = 0;
        private Color _TrackColor = Color.DarkGray;
        private Color _TextColor = Color.Black;
        private Color _DragColorInner = Color.DarkSlateGray;
        private Image _DraggerOverride = null;
        private Rectangle bounds;
        private TrackType _TrackMinor = TrackType.None;
        private TrackType _TrackMayor = TrackType.None;
        private StringFormat _StringFormat = new StringFormat() {
            LineAlignment = StringAlignment.Center,
            Alignment = StringAlignment.Center,
        };
        #endregion
        #region public properties
        public enum TrackType { None, Top, Bottom, Both, Full };

        [DefaultValue(typeof(Color), "Green")]
        public override Color ForeColor { get => base.ForeColor; set => base.ForeColor = value; }

        [DefaultValue(typeof(Color), "LightGray")]
        public override Color BackColor { get => base.BackColor; set => base.BackColor = value; }

        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        public override string Text { get => base.Text; set => base.Text = value; }

        /// <summary>
        /// The StringFormat used when drawing the Text.
        /// </summary>
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        [Category("Appearance")]
        [DisplayName("StringFormat")]
        [Description("The StringFormat used when drawing the Text.")]
        public StringFormat StringFormat {
            get { return _StringFormat; }
            set { _StringFormat = value; Redraw(LayerNames.textLayer); }
        }

        /// <summary>
        /// Whether to allow the user to change the progressbar value directly.
        /// </summary>
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        [Category("Behavior")]
        [Description("Whether to allow the user to change the progressbar value directly.")]
        [DefaultValue(typeof(bool), "false")]
        public bool AllowUserDragging { get { return _AllowUserDragging; } set { _AllowUserDragging = value; } }

        /// <summary>
        /// Assigning an image here will replace the dragger with your image.
        /// </summary>
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        [Category("Design")]
        [DisplayName("DraggerOverride")]
        [Description("Assigning an image here will replace the dragger with your image.")]
        [DefaultValue(null)]
        public Image DraggerOverride {
            get { return _DraggerOverride; }
            set { _DraggerOverride = value; Redraw(LayerNames.draggerLayer); }
        }

        /// <summary>
        /// From what side of the control the minor markings are drawn; 'Full' will draw a complete line from top to bottom instead.
        /// </summary>
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        [Category("Design")]
        [DisplayName("MinorTrackmarkingDesign")]
        [Description("From what side of the control the minor markings are drawn; 'Full' will draw a complete line from top to bottom instead.")]
        [DefaultValue(typeof(TrackType), "None")]
        public TrackType TrackMinor { get { return _TrackMinor; } set { _TrackMinor = value; Redraw(LayerNames.trackLayer); } }

        /// <summary>
        /// From what side of the control the mayor markings are drawn; 'Full' will draw a complete line from top to bottom instead.
        /// </summary>
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        [Category("Design")]
        [DisplayName("MayorTrackmarkingDesign")]
        [Description("From what side of the control the mayor markings are drawn; 'Full' will draw a complete line from top to bottom instead.")]
        [DefaultValue(typeof(TrackType), "None")]
        public TrackType TrackMayor { get { return _TrackMayor; } set { _TrackMayor = value; Redraw(LayerNames.trackLayer); } }

        /// <summary>
        /// The interval at which the minor markings are drawn on the control. If 0 no minor markings are drawn.
        /// </summary>
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        [Category("Design")]
        [DisplayName("MinorTrackmarkingInterval")]
        [Description("The interval at which the minor markings are drawn on the control. If 0 no minor markings are drawn.")]
        [DefaultValue(typeof(ushort), "2")]
        public ushort TrackStepMinor { get { return _TrackStepMinor; } set { _TrackStepMinor = value; Redraw(LayerNames.trackLayer); } }

        /// <summary>
        /// The interval at which the mayor markings are drawn on the control. If 0 no mayor markings are drawn.
        /// </summary>
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        [Category("Design")]
        [DisplayName("MayorTrackmarkingInterval")]
        [Description("The interval at which the mayor markings are drawn on the control. If 0 no mayor markings are drawn.")]
        [DefaultValue(typeof(ushort), "10")]
        public ushort TrackStepMayor { get { return _TrackStepMayor; } set { _TrackStepMayor = value; Redraw(LayerNames.trackLayer); } }

        /// <summary>
        /// The Maximum allowed Value.
        /// </summary>
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        [Category("Data")]
        [DisplayName("Maximum")]
        [Description("The Maximum allowed Value.")]
        [DefaultValue(typeof(int), "100")]
        public int Maximum {
            get { return _Maximum; }
            set {
                if (value > _Minimum) {
                    if (_Value > _Maximum) {
                        Trace.WriteLine(Name + ": New Maximum (" + value.ToString() + ") invalidated Value (" + _Value.ToString() + "); Value has been set to the new Maximum.");
                        _Value = _Maximum;
                    }
                    _Maximum = value;
                    Redraw(LayerNames.foregroundLayer);
                } else {
                    throw new ArgumentOutOfRangeException(paramName: "Maximum", actualValue: value, message: "The new value must be higher than the Minimum property (" + _Minimum.ToString() + ")");
                }
            }
        }

        /// <summary>
        /// The Minimum allowed Value.
        /// </summary>
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        [Category("Data")]
        [DisplayName("Minimum")]
        [Description("The Minimum allowed Value.")]
        [DefaultValue(typeof(int), "0")]
        public int Minimum {
            get { return _Minimum; }
            set {
                if (value < _Maximum) {
                    if (_Value < _Minimum) {
                        Trace.WriteLine(Name + ": New Minimum (" + value.ToString() + ") invalidated Value (" + _Value.ToString() + "); Value has been set to the new Minimum.");
                        _Value = _Minimum;
                    }
                    _Minimum = value;
                    Redraw(LayerNames.foregroundLayer);
                } else {
                    throw new ArgumentOutOfRangeException(paramName: "Minimum", actualValue: value, message: "The new value must be lower than the Maximum property (" + _Maximum.ToString() + ")");
                }
            }
        }

        /// <summary>
        /// The current position of the TrackedProgressBar.
        /// </summary>
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        [Category("Data")]
        [DisplayName("Value")]
        [Description("The current position of the TrackedProgressBar.")]
        [DefaultValue(typeof(int), "0")]
        public int Value {
            get { return _Value; }
            set {
                if (value >= _Minimum & value <= _Maximum) {
                    ValueChanged?.Invoke(this, e: (_Value, value));
                    _Value = value;
                    Redraw(LayerNames.foregroundLayer);
                } else {
                    throw new ArgumentOutOfRangeException(paramName: "Value", actualValue: value, message: "The new value must be in the range of the Minimum and Maximum property: [" + _Minimum.ToString() + ":" + _Maximum.ToString() + "]");
                }
            }
        }

        /// <summary>
        /// The color of the trackmarkings.
        /// </summary>
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        [Category("Appearance")]
        [DisplayName("TrackColor")]
        [Description("The color of the trackmarkings.")]
        [DefaultValue(typeof(Color), "DarkGray")]
        public Color TrackColor { get { return _TrackColor; } set { ColorChanged?.Invoke(this, e: ("TrackColor", _TrackColor, value)); _TrackColor = value; Redraw(LayerNames.trackLayer); } }

        /// <summary> 
        /// The color of the text from the control.
        /// </summary>
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        [Category("Appearance")]
        [DisplayName("TextColor")]
        [Description("The color of the text from the control.")]
        [DefaultValue(typeof(Color), "Black")]
        public Color TextColor { get { return _TextColor; } set { ColorChanged?.Invoke(this, e: ("TextColor", _TextColor, value)); _TextColor = value; Redraw(LayerNames.trackLayer); } }

        /// <summary> 
        /// The inner/background color of the Dragger.
        /// </summary>
        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        [Category("Appearance")]
        [DisplayName("DraggerColor")]
        [Description("The inner/background color of the Dragger.")]
        [DefaultValue(typeof(Color), "Color.DarkSlateGray")]
        public Color DraggerColor { get { return _DragColorInner; } set { ColorChanged?.Invoke(this, e: ("DraggerColor", _DragColorInner, value)); _DragColorInner = value; Redraw(LayerNames.draggerLayer); } }
        #endregion
        #region events
        public event EventHandler<(string param, Color oldColor, Color newColor)> ColorChanged;
        public event EventHandler<(int oldValue, int newValue)> ValueChanged;
        protected override void OnForeColorChanged(EventArgs e) {
            base.OnForeColorChanged(e);
            Redraw(LayerNames.foregroundLayer);
        }
        protected override void OnTextChanged(EventArgs e) {
            base.OnTextChanged(e);
            Redraw(LayerNames.textLayer);
        }
        protected override void OnFontChanged(EventArgs e) {
            base.OnFontChanged(e);
            Redraw(LayerNames.textLayer);
        }
        protected override void OnBackColorChanged(EventArgs e) {
            base.OnBackColorChanged(e);
            if (BackgroundImage == null) {
                Redraw(LayerNames.backgroundLayer);
            }
        }
        protected override void OnBackgroundImageChanged(EventArgs e) {
            base.OnBackgroundImageChanged(e);
            Redraw(LayerNames.backgroundLayer);
        }
        protected override void OnClientSizeChanged(EventArgs e) {
            // https://social.msdn.microsoft.com/Forums/vstudio/en-US/c3e12273-f53f-4b44-8053-987b0fcf4933/difference-between-clientsizechanged-size-changed-and-resize-events
            base.OnClientSizeChanged(e);
            if (!ResizeRedraw | isDisposing) {
                return;
            }
            RecreateBuffers();
            Redraw(LayerNames.backgroundLayer);
        }
        protected override void OnMouseLeave(EventArgs e) {
            base.OnMouseLeave(e);
            if (!_AllowUserDragging | isDisposing) {
                return;
            }
            showDragger = false;
            Redraw(LayerNames.draggerLayer);
        }
        protected override void OnMouseEnter(EventArgs e) {
            base.OnMouseEnter(e);
            if (!_AllowUserDragging | isDisposing) {
                return;
            }
            showDragger = true;
            Redraw(LayerNames.draggerLayer);
        }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            if (!_AllowUserDragging | isDisposing) {
                return;
            }
            Capture = true;
            isDragging = true;
            SetDragValue(new Point(e.X, e.Y));
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            if (!_AllowUserDragging | !isDragging | isDisposing) {
                return;
            }
            SetDragValue(new Point(e.X, e.Y));
        }
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            if (!_AllowUserDragging | !isDragging | isDisposing) {
                return;
            }
            Capture = false;
            isDragging = false;
            OnValueChanged(_Value, dragValue);
            _Value = dragValue;
            Redraw(LayerNames.foregroundLayer);
        }
        protected virtual void OnValueChanged(int oldValue, int newValue) => ValueChanged?.Invoke(this, (oldValue, newValue));
        #endregion
        #region constructor
        /// <summary>Initializes a new instance of the TrackedProgressBar class in its default state.</summary>
        /// <remarks>Created with the information from:<list type="bullet">
        /// <item><description><see href="link">https://www.inchoatethoughts.com/custom-drawing-controls-in-c-manual-double-buffering</see></description></item>
        /// <item><description><see href="link">https://www.codeproject.com/articles/84833/drawing-multiple-layers-without-flicker</see></description></item>
        /// <item><description><see href="link">https://docs.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-create-a-windows-forms-control-that-shows-progress?view=netframeworkdesktop-4.8</see></description></item>
        /// <item><description><see href="link">https://github.com/dotnet/winforms/tree/69a326a9345471c26c5c1e4b9881a7b43e5b587c/src/System.Windows.Forms/src/System/Windows/Forms</see></description></item>
        /// </list></remarks>
        public TrackedProgressBar() : base() {
            InitializeComponent();

            // Change defaults
            AutoScaleDimensions = new SizeF(200F, 20F);
            ClientSize = new Size(200, 20);
            BackColor = Color.LightGray;
            ForeColor = Color.Green;
            SetStyle(ControlStyles.ResizeRedraw, true);

            // Set the control style to double buffer.
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, false);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            // Assign our buffer context and initialise the array.
            backBufferContext = BufferedGraphicsManager.Current;
            backBufferGraphic = new BufferedGraphics[Layers];
            initializationComplete = true;

            RecreateBuffers();
        }
        #endregion
        #region drawing
        protected override void OnPaint(PaintEventArgs e) {
            if (!isDisposing) {
                backBufferGraphic[4]?.Render(e.Graphics);
            }
            base.OnPaint(e);
        }
        /// <summary>Gets the <see cref="Control.ClientRectangle"/> and uses it for the new <see cref="BufferedGraphics"/> instances.</summary>
        private void RecreateBuffers() {
            // Check initialization has completed so we know backBufferContext has been assigned.
            // Check that we aren't disposing or this could be invalid.
            if (!initializationComplete | isDisposing) return;

            // The size of the buffer
            bounds = ClientRectangle;

            // We recreate the buffer with a width and height of the control. The "+ 1"
            // guarantees we never have a buffer with a width or height of 0.
            backBufferContext.MaximumBuffer = new Size(bounds.Width + 1, bounds.Height + 1);

            for (byte i = 0; i < Layers; i++) {
                // Dispose of old backBufferGraphic (if one has been created already)
                // if (backBufferGraphic[i] != null) backBufferGraphic[i].Dispose();
                backBufferGraphic[i]?.Dispose();

                // Create new backBufferGraphic that matches the current size of buffer.
                backBufferGraphic[i] = backBufferContext.Allocate(CreateGraphics(), bounds);

                // This is a good place to assign drawingGraphics.SmoothingMode if you want a better anti-aliasing technique.
            }

            // Recreate content
            Redraw(LayerNames.backgroundLayer);
        }
        private void Redraw(LayerNames startLayer) {
            // Check if the buffers are allocated and we are not disposing.
            if (isDisposing | backBufferGraphic == null) return;
            // backBufferGraphic[0].Graphics = backgroundLayer
            if (startLayer == LayerNames.backgroundLayer) {
                backBufferGraphic[0].Graphics.Clear(BackColor);
                if (BackgroundImage != null) backBufferGraphic[0].Graphics.FillRectangle(new TextureBrush(BackgroundImage), bounds);
            }
            // backBufferGraphic[1].Graphics = foregroundLayer
            if ((byte)startLayer <= 1) {
                backBufferGraphic[0].Render(backBufferGraphic[1].Graphics);
                // The progress bar completion.
                if (ForeColor != Color.Empty & _Value != _Minimum)
                    backBufferGraphic[1].Graphics.FillRectangle(new SolidBrush(ForeColor), new Rectangle(0, 0, width: (int)(((float)(_Value - _Minimum) / (_Maximum - _Minimum)) * Math.Max(bounds.Width, 1)), Math.Max(bounds.Height, 1)));
            }
            // backBufferGraphic[2].Graphics = trackLayer
            if ((byte)startLayer <= 2) {
                backBufferGraphic[1].Render(backBufferGraphic[2].Graphics);
                if (_TrackColor != Color.Empty & (_TrackMayor != TrackType.None | _TrackMinor != TrackType.None)) {
                    float offset = (float)Math.Max(bounds.Width, 1) / (_Maximum - _Minimum);
                    Pen trackPen = new Pen(color: _TrackColor);
                    if (_TrackStepMinor > 0 & _TrackMinor != TrackType.None) {    // Minor lines
                        if (_TrackMinor == TrackType.Full) {
                            for (int i = _TrackStepMinor; i < (_Maximum - _Minimum); i += _TrackStepMinor)
                                backBufferGraphic[2].Graphics.DrawLine(trackPen,
                                    new Point(x: (int)(i * offset) - 1, y: 0),
                                    new Point(x: (int)(i * offset) - 1, y: Math.Max(bounds.Height, 1))
                                );
                        } else {
                            if (_TrackMinor == TrackType.Both | _TrackMinor == TrackType.Bottom) {
                                for (int i = _TrackStepMinor; i < (_Maximum - _Minimum); i += _TrackStepMinor)
                                    backBufferGraphic[2].Graphics.DrawLine(trackPen,
                                        new Point(x: (int)(i * offset) - 1, y: Math.Max(bounds.Height, 1)),
                                        new Point(x: (int)(i * offset) - 1, y: Convert.ToInt32(Math.Max(bounds.Height, 1) * .75))
                                    );
                            }
                            if (_TrackMinor == TrackType.Both | _TrackMinor == TrackType.Top) {
                                for (int i = _TrackStepMinor; i < (_Maximum - _Minimum); i += _TrackStepMinor)
                                    backBufferGraphic[2].Graphics.DrawLine(trackPen,
                                        new Point(x: (int)(i * offset) - 1, y: Convert.ToInt32(Math.Max(bounds.Height, 1) * .25)),
                                        new Point(x: (int)(i * offset) - 1, y: 0)
                                    );
                            }
                        }
                    }
                    if (_TrackStepMayor > 0 & _TrackMayor != TrackType.None) {    // Mayor lines
                        if (_TrackMayor == TrackType.Full) {
                            for (int i = _TrackStepMayor; i < (_Maximum - _Minimum); i += _TrackStepMayor) {
                                backBufferGraphic[2].Graphics.DrawLine(trackPen,
                                    new Point(x: (int)(i * offset) - 1, y: 0),
                                    new Point(x: (int)(i * offset) - 1, y: Math.Max(bounds.Height, 1))
                                );
                            }
                        } else {
                            if (_TrackMayor == TrackType.Both | _TrackMayor == TrackType.Bottom) {
                                for (int i = _TrackStepMayor; i < (_Maximum - _Minimum); i += _TrackStepMayor) {
                                    backBufferGraphic[2].Graphics.DrawLine(trackPen,
                                        new Point(x: (int)(i * offset) - 1, y: Math.Max(bounds.Height, 1)),
                                        new Point(x: (int)(i * offset) - 1, y: Convert.ToInt32(Math.Max(bounds.Height, 1) * .6))
                                    );
                                }
                            }
                            if (_TrackMayor == TrackType.Both | _TrackMayor == TrackType.Top) {
                                for (int i = _TrackStepMayor; i < (_Maximum - _Minimum); i += _TrackStepMayor) {
                                    backBufferGraphic[2].Graphics.DrawLine(trackPen,
                                        new Point(x: (int)(i * offset) - 1, y: Convert.ToInt32(Math.Max(bounds.Height, 1) * .4)),
                                        new Point(x: (int)(i * offset) - 1, y: 0)
                                    );
                                }
                            }
                        }
                    }
                }
            }
            // backBufferGraphic[3].Graphics = textLayer
            if ((byte)startLayer <= 3) {
                backBufferGraphic[2].Render(backBufferGraphic[3].Graphics);
                if (Text != "" & TextColor != Color.Empty) {
                    backBufferGraphic[3].Graphics.DrawString(s: Text, font: Font, brush: new SolidBrush(color: _TextColor), layoutRectangle: bounds, format: _StringFormat);
                }
            }
            backBufferGraphic[3].Render(backBufferGraphic[4].Graphics);
            // backBufferGraphic[4].Graphics = draggerLayer
            if (_AllowUserDragging & (showDragger | isDragging)) {
                int dragLoc;
                if (isDragging)
                    dragLoc = (int)(((float)(dragValue - _Minimum) / (_Maximum - _Minimum)) * Math.Max(bounds.Width, 1)) - 1;
                else
                    dragLoc = (int)(((float)(_Value - _Minimum) / (_Maximum - _Minimum)) * Math.Max(bounds.Width, 1)) - 1;
                if (showDragger | isDragging) {
                    if (_DraggerOverride == null) {
                        if (_DragColorInner != Color.Empty) {
                            backBufferGraphic[4].Graphics.FillRectangle(new SolidBrush(_DragColorInner), dragLoc - 4, 0, 8, Math.Max(bounds.Height, 1));
                        }
                        if (_TrackColor != Color.Empty) {
                            backBufferGraphic[4].Graphics.DrawRectangle(new Pen(new SolidBrush(_TrackColor)), dragLoc - 4, 0, 8, Math.Max(bounds.Height, 1));
                            backBufferGraphic[4].Graphics.DrawLine(new Pen(new SolidBrush(_TrackColor)), dragLoc, (int)(bounds.Height * .4), dragLoc, (int)(bounds.Height * .6));
                        }
                    } else {
                        backBufferGraphic[4].Graphics.DrawImage(_DraggerOverride, dragLoc - 4, 0, 8, Math.Max(bounds.Height, 1));
                    }
                }
            }
            // Invalidate the control so a repaint gets called somewhere down the line.
            Invalidate();
            // Alternatively one could force the control to both invalidate and update by replacing this with:
            // Refresh();
        }
        private void SetDragValue(Point mouseLocation) {
            if (bounds.Contains(mouseLocation)) {
                float percentage = (float)mouseLocation.X / Math.Max(bounds.Width, 1);
                int newDragValue = (int)(percentage * (_Maximum - _Minimum)) + _Minimum;
                if (newDragValue != dragValue) dragValue = newDragValue;
            } else {
                if (bounds.Y <= mouseLocation.Y && mouseLocation.Y <= bounds.Y + bounds.Height) {
                    if (mouseLocation.X <= bounds.X && mouseLocation.X > bounds.X - 10) {
                        int newDragValue = _Minimum;
                        if (newDragValue != dragValue) dragValue = newDragValue;
                    } else if (mouseLocation.X >= bounds.X + bounds.Width && mouseLocation.X < bounds.X + bounds.Width + 10) {
                        int newDragValue = _Maximum;
                        if (newDragValue != dragValue) dragValue = newDragValue;
                    }
                } else {
                    if (dragValue != Value) dragValue = Value;
                }
            }
            Redraw(LayerNames.draggerLayer);
        }
        #endregion
    }
}
