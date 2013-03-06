///////////////////////////////////////////////////////////////////////////////////
// Open 3D Model Viewer (open3mod) (v0.1)
// [Renderer.cs]
// (c) 2012-2013, Alexander C. Gessler
//
// Licensed under the terms and conditions of the 3-clause BSD license. See
// the LICENSE file in the root folder of the repository for the details.
//
// HIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE 
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
// ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; 
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND 
// ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT 
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS 
// SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
///////////////////////////////////////////////////////////////////////////////////


using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using System.Diagnostics;

namespace open3mod
{
    public class Renderer : IDisposable
    {
        private readonly MainWindow _window;
        private readonly TextOverlay _textOverlay;

        private Image[,] _hudImages;
        private bool _hudDirty = true;

        private double _accTime;
        private readonly float[] _lastActiveVp = new float[4];
        private Point _mousePos;
        private Rectangle _hoverRegion;
        private double _displayFps;


        /// <summary>
        /// The gl context which is being rendered to
        /// </summary>
        public GLControl GlControl { get { return _window.GlControl; }} 

        /// <summary>
        /// Host window
        /// </summary>
        public MainWindow Window { get { return _window; } }


        /// <summary>
        /// Utility object in charge of maintaining all text overlays
        /// </summary>
        public TextOverlay TextOverlay { get { return _textOverlay; } }

        /// <summary>
        /// Obtain actual rendering resolution in pixels
        /// </summary>
        public Size RenderResolution { get { return GlControl.ClientSize; } }


        /// <summary>
        /// Construct a renderer given a valid and fully loaded MainWindow
        /// </summary>
        /// <param name="window">Main window, Load event of the GlContext
        ///    needs to be fired already.</param>
        internal Renderer(MainWindow window)
        {
            _window = window;
            _textOverlay = new TextOverlay(this);
        }


        /// <summary>
        /// Perform any non-drawing operations that need to be executed
        /// once per frame and whose implementation resides in Renderer.
        /// </summary>
        public void Update()
        {
            
        }


        /// <summary>
        /// Draw the contents of a given tab. If the tab contains a scene,
        /// this scene is drawn. If the tab is in loading or failed state,
        /// the corresponding info screen will be drawn.
        /// </summary>
        /// <param name="activeTab">Tab containing the scene to be drawn</param>
        public void Draw(Tab activeTab)
        {
            GL.ClearColor(Color.LightGray);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            var ui = Window.UiState.ActiveTab;

            var index = Tab.ViewIndex.Index0;
            foreach (var view in ui.ActiveViews)
            {
                // draw the active viewport last (to make sure its contour line is on top)
                if (view == null || ui.ActiveViewIndex == index)
                {
                    ++index;
                    continue;
                }

                var cam = ui.ActiveCameraControllerForView(index);
                DrawViewport(cam, activeTab, view.Value.X, view.Value.Y, view.Value.Z, view.Value.W, false);
                ++index;
            }

            var activeVp = ui.ActiveViews[(int)ui.ActiveViewIndex];
            Debug.Assert(activeVp != null);
            DrawViewport(ui.ActiveCameraController, activeTab, activeVp.Value.X, activeVp.Value.Y, 
                activeVp.Value.Z, activeVp.Value.W, true);

            if (ui.ActiveViewMode != Tab.ViewMode.Single)
            {
                SetFullViewport();
            }

            if (Window.UiState.ShowFps)
            {
                DrawFps();
            }

            DrawHud(activeVp.Value.X, activeVp.Value.Y, activeVp.Value.Z, activeVp.Value.W);           
            _textOverlay.Draw();
        }


        /// <summary>
        /// Draw HUD (camera panel) given current active viewport pos, dim
        /// </summary>
        /// <param name="x1"></param>
        /// <param name="y1"></param>
        /// <param name="x2"></param>
        /// <param name="y2"></param>
        /// <param name="activeTab"> </param>
        private void DrawHud(float x1, float y1, float x2, float y2)
        {
            if(!_hudDirty)
            {
// ReSharper disable CompareOfFloatsByEqualityOperator
                _hudDirty = x1 != _lastActiveVp[0] || y1 != _lastActiveVp[1] || x2 != _lastActiveVp[2] || y2 != _lastActiveVp[3];
// ReSharper restore CompareOfFloatsByEqualityOperator
            }

            _lastActiveVp[0] = x1;
            _lastActiveVp[1] = y1;
            _lastActiveVp[2] = x2;
            _lastActiveVp[3] = y2;

            if (!_textOverlay.WantRedraw)
            {
                if (_hudDirty)
                {
                    _textOverlay.WantRedrawNextFrame = true;
                }
                return;
            }

            _hudDirty = false;

            LoadHudImages();
            Debug.Assert(_hudImages != null);

            var graphics = _textOverlay.GetDrawableGraphicsContext();
            var xPoint = (int) ((x1+x2)*(double)RenderResolution.Width);
            const int yPoint = 3;
            const int xSpacing = 4;

            var imageWidth = _hudImages[0, 0].Width;
            var imageHeight = _hudImages[0, 0].Height;

            var regionWidth = imageWidth * _hudImages.GetLength(0) + xSpacing * (_hudImages.GetLength(0)-1);
            const int regionHeight = 25;

            xPoint -= regionWidth;
            _hoverRegion = new Rectangle(xPoint, yPoint, regionWidth - 2, regionHeight);

            graphics.FillRectangle(new SolidBrush(Color.FromArgb(50, 100, 100, 100)), _hoverRegion);

            xPoint += _hudImages.GetLength(0)/2;
            for (var i = 0; i < _hudImages.GetLength(0); ++i)
            {
                var x = xPoint;
                var y = yPoint + 2;
                var w = (int) (imageWidth*2.0/3);
                var h = (int) (imageHeight*2.0/3);

                var ui = Window.UiState.ActiveTab;
 
                // normal image
                int imageIndex = 0;
                if(ui.ActiveCameraController.GetCameraType() == (CameraType)i)
                {
                    // selected image
                    imageIndex = 2;
                }
                else if (_mousePos.X > x && _mousePos.X <= x + w && _mousePos.Y > y && _mousePos.Y <= y + h)
                {
                    // hover image
                    imageIndex = 1;
                }

                var img = _hudImages[i, imageIndex];
                Debug.Assert(img.Width == imageWidth && img.Height == imageHeight, "all images must be of the same size");

                graphics.DrawImage(img, x,y,w,h);
                xPoint += img.Width;
            }
        }


        public void OnMouseMove(MouseEventArgs mouseEventArgs)
        {
            _mousePos = mouseEventArgs.Location;
            if( _mousePos.X > _hoverRegion.Left && _mousePos.X <= _hoverRegion.Right &&
                _mousePos.Y > _hoverRegion.Top  && _mousePos.Y <= _hoverRegion.Bottom)
            {
                _hudDirty = true;
            }
        }


        /// <summary>
        /// Populate _hudImages
        /// </summary>
        private void LoadHudImages()
        {
            if (_hudImages == null)
            {
                _hudImages = new Image[5,3];
                var prefixTable = new[]
                {
                    "open3mod.Images.HUD_X",
                    "open3mod.Images.HUD_Y",
                    "open3mod.Images.HUD_Z",
                    "open3mod.Images.HUD_Orbit",
                    "open3mod.Images.HUD_FPS"
                };

                var postFixTable = new[]
                {
                    "_Normal",
                    "_Hover",
                    "_Selected"
                };

                for (var i = 0; i < _hudImages.GetLength(0); ++i)
                {
                    for (var j = 0; j < _hudImages.GetLength(1); ++j)
                    {
                        _hudImages[i, j] = ImageFromResource.Get(prefixTable[i] + postFixTable[j] + ".png");
                    }
                }
            }
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }


        public virtual void Dispose(bool disposing)
        {
            _textOverlay.Dispose();          
        }


        /// <summary>
        /// Respond to window changes. Users normally do not need to call this.
        /// </summary>
        public void Resize()
        {
            _textOverlay.Resize();
        }


        /// <summary>
        /// Draw a scene to a viewport using an ICameraController to specify the camera.
        /// </summary>
        /// <param name="view">Active cam controller for this viewport</param>
        /// <param name="activeTab">Scene to be drawn</param>
        /// <param name="xs">X-axis starting point of the viewport in range [0,1]</param>
        /// <param name="ys">Y-axis starting point of the viewport in range [0,1]</param>
        /// <param name="xe">X-axis end point of the viewport in range [0,1]</param>
        /// <param name="ye">X-axis end point of the viewport in range [0,1]</param>
        /// <param name="active"></param>
        private void DrawViewport(ICameraController view, Tab activeTab, double xs, double ys, double xe, 
            double ye, bool active = false)
        {
            // update viewport 
            var w = (double)RenderResolution.Width;
            var h = (double)RenderResolution.Height;

            var vw = (int) ((xe-xs)*w);
            var vh = (int) ((ye-ys)*h);
            GL.Viewport((int)(xs * w), (int)(ys * h), (int)((xe - xs) * w), (int)((ye - ys) * h));

            DrawViewportColorsPre(active);
            var aspectRatio = (float) ((xe - xs) / (ye - ys));

            // set a proper perspective matrix for rendering
            Matrix4 perspective = Matrix4.CreatePerspectiveFieldOfView(MathHelper.PiOver4, aspectRatio, 0.001f, 100.0f);
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadMatrix(ref perspective);          

            if (activeTab.ActiveScene == null)
            {
                if (activeTab.State == Tab.TabState.Failed)
                {
                    DrawFailureSplash(activeTab.ErrorMessage);
                }
                else if (activeTab.State == Tab.TabState.Loading)
                {
                    DrawLoadingSplash();
                }
                else
                {
                    Debug.Assert(activeTab.State == Tab.TabState.Empty);
                    DrawNoSceneSplash();
                }
            }
            else
            {
                DrawScene(activeTab.ActiveScene, view);
            }
           
            DrawViewportColorsPost(active, vw, vh);
        }



        private void SetFullViewport()
        {
            GL.Viewport(0, 0, RenderResolution.Width, RenderResolution.Height);
        }


        private void DrawViewportColorsPre(bool active)
        {
            if (!active)
            {
                return;
            }
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadIdentity();
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();

            // paint the active viewport in a slightly different shade of gray,
            // overwriting the initial background color.
            GL.Color4(Color.DarkGray);
            GL.Rect(-1, -1, 1, 1);
        }


        private void DrawViewportColorsPost(bool active, int width, int height)
        {
            GL.Hint(HintTarget.LineSmoothHint, HintMode.Nicest);

            var texW = 1.0/width;
            var texH = 1.0/height;

            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadIdentity();
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();

            var lineWidth = active ? 4 : 3;

            // draw contour line
            GL.LineWidth(lineWidth);
            GL.Color4(active ? Color.GreenYellow : Color.DarkGray);

            var xofs = lineWidth * 0.5 * texW;
            var yofs = lineWidth * 0.5 * texH;

            GL.Begin(BeginMode.LineStrip);
            GL.Vertex2(-1.0 + xofs, -1.0 + yofs);
            GL.Vertex2(1.0 - xofs, -1.0 + yofs);
            GL.Vertex2(1.0 - xofs, 1.0 - yofs);
            GL.Vertex2(-1.0 + xofs, 1.0 - yofs);
            GL.Vertex2(-1.0 + xofs, -1.0 + yofs);
            GL.End();

            GL.LineWidth(1);
            GL.MatrixMode(MatrixMode.Modelview);
        }


        private void DrawScene(Scene scene, ICameraController view)
        {
            Debug.Assert(scene != null);
            scene.Render(Window.UiState, view);
        }


        private void DrawNoSceneSplash()
        {
            var graphics = _textOverlay.GetDrawableGraphicsContext();

            var format = new StringFormat();
            format.LineAlignment = StringAlignment.Center;
            format.Alignment = StringAlignment.Center;

            graphics.DrawString("Drag file here", Window.UiState.DefaultFont16, 
                new SolidBrush(Color.Black),  
                new RectangleF(0,0,GlControl.Width,GlControl.Height),
                format);
        }


        private void DrawLoadingSplash()
        {
            var graphics = _textOverlay.GetDrawableGraphicsContext();

            var format = new StringFormat {LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Center};

            graphics.DrawString("Loading ...", Window.UiState.DefaultFont16,
                new SolidBrush(Color.Black),
                new RectangleF(0, 0, GlControl.Width, GlControl.Height),
                format);
        }


        private void DrawFailureSplash(string message)
        {
            var graphics = _textOverlay.GetDrawableGraphicsContext();

            var format = new StringFormat {LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Center};

            // hack: re-use the image we use for failed texture imports :-)
            var img = TextureThumbnailControl.GetLoadErrorImage();

            graphics.DrawImage(img, GlControl.Width / 2 - img.Width / 2, GlControl.Height / 2 - img.Height - 30,img.Width,img.Height);
            graphics.DrawString("Sorry, this scene failed to load.", Window.UiState.DefaultFont16, 
                new SolidBrush(Color.Red), 
                new RectangleF(0,0,GlControl.Width,GlControl.Height),
                format);

            graphics.DrawString("What the importer said went wrong: " + message, Window.UiState.DefaultFont12,
                new SolidBrush(Color.Black),
                new RectangleF(0, 100, GlControl.Width, GlControl.Height),
                format);
        }


        private void DrawFps()
        {
            // only update every 1/3rd of a second
            _accTime += Window.Fps.LastFrameDelta;
            if (_accTime < 0.3333 && !_textOverlay.WantRedraw)
            {
                if (_accTime >= 0.3333)
                {
                    _textOverlay.WantRedrawNextFrame = true;
                }
                return;
            }

            if (_accTime >= 0.3333)
            {              
                _displayFps = Window.Fps.LastFps;
                _accTime = 0.0;      
            }
          
            var graphics = _textOverlay.GetDrawableGraphicsContext();
            graphics.DrawString("FPS: " + _displayFps.ToString("0.0"), Window.UiState.DefaultFont12,
                                new SolidBrush(Color.Red), 5, 5);
        }       
    }
}

/* vi: set shiftwidth=4 tabstop=4: */ 