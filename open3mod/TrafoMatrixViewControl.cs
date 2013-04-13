﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Assimp;
using OpenTK;
using Quaternion = Assimp.Quaternion;

namespace open3mod
{
    /// <summary>
    /// Visualizes the translation, scaling and rotation parts of a transformation
    /// matrix.
    /// </summary>
    public partial class TrafoMatrixViewControl : UserControl
    {
        private Vector3D _scale;
        private Quaternion _rot;
        private Vector3D _trans;
        private Matrix4x4 _baseMatrix;

        private Quaternion _rotCurrent;
        private bool _isInDiffView;

        private const string Format = "#0.000";

        private enum RotationMode
        {
            // note: this directly corresponds to the UI combobox, keep in sync!
            EulerXyzDegrees = 0,
            EulerXyzRadians = 1,
            Quaternion = 2
        }


        public TrafoMatrixViewControl()
        {
            InitializeComponent();

            comboBoxRotMode.SelectedIndex = CoreSettings.CoreSettings.Default.DefaultRotationMode < comboBoxRotMode.Items.Count 
                ? CoreSettings.CoreSettings.Default.DefaultRotationMode : 0;

            // workaround based on
            // http://stackoverflow.com/questions/276179/how-to-change-the-font-color-of-a-disabled-textbox
            // WinForms only draws custom FG colors for readonly text boxes if an internal "ColorCustomized"
            // flag is set. To set this flag, we have to assign to the BackColor at least once.
            foreach (var cc in Controls.OfType<Control>())
            {
                cc.BackColor = cc.BackColor;
            }
        }


        /// <summary>
        /// Update the display. This involves decomposing the matrix and is
        /// therefore an expensive operation.
        /// </summary>
        /// <param name="mat"></param>
        public void SetMatrix(ref Matrix4x4 mat)
        {
            UpdateUi(mat, false);
            _baseMatrix = mat;
        }


        /// <summary>
        /// Sets an animated matrix to be displayed instead of the last matrix
        /// set via SetMatrix(). Changed components are highlighted in the UI.
        /// </summary>
        /// <param name="mat"></param>
        public void SetAnimatedMatrix(ref Matrix4x4 mat)
        {
            _isInDiffView = true;
            UpdateUi(mat, true);  
        }


        /// <summary>
        /// Set the display back to the last matrix set via SetMatrix()
        /// </summary>
        public void ResetAnimatedMatrix()
        {
            _isInDiffView = false;
            UpdateUi(_baseMatrix, false);
        }


        private void OnUpdateRotation(object sender, EventArgs e)
        {
            CoreSettings.CoreSettings.Default.DefaultRotationMode = comboBoxRotMode.SelectedIndex;
            SetRotation(_isInDiffView);
        }


        private void UpdateUi(Matrix4x4 mat, bool diffAgainstBaseMatrix)
        {
            // use assimp math data structures because they have Decompose()
   
            // the decomposition algorithm is not very sophisticated - it basically extracts translation
            // and row scaling factors and then converts the rest to a quaternion. 
            // question: what if the matrix is non-invertible? the algorithm then yields
            // at least one scaling factor as zero, further results are undefined. We
            // therefore catch this case by checking the determinant and inform the user
            // that the results may be wrong.
            checkBoxNonStandard.Checked = Math.Abs(mat.Determinant()) < 1e-5;

            Vector3D scale;
            Quaternion rot;
            Vector3D trans;

            mat.Decompose(out scale, out rot, out trans);

            // translation
            textBoxTransX.Text = trans.X.ToString(Format);
            textBoxTransY.Text = trans.Y.ToString(Format);
            textBoxTransZ.Text = trans.Z.ToString(Format);

            // scaling - simpler view mode for uniform scalings
            var isUniform = Math.Abs(scale.X - scale.Y) < 1e-5 && Math.Abs(scale.X - scale.Z) < 1e-5;
            textBoxScaleX.Text = scale.X.ToString(Format);
            textBoxScaleY.Text = scale.Y.ToString(Format);
            textBoxScaleZ.Text = scale.Z.ToString(Format);

            textBoxScaleY.Visible = !isUniform;
            textBoxScaleZ.Visible = !isUniform;
            labelScalingX.Visible = !isUniform;
            labelScalingY.Visible = !isUniform;
            labelScalingZ.Visible = !isUniform;

            if (diffAgainstBaseMatrix)
            {
                const double epsilon = 1e-5f;
                if (Math.Abs(scale.X-_scale.X) > epsilon)
                {
                    labelScalingX.ForeColor = ColorIsAnimated;
                    textBoxScaleX.ForeColor = ColorIsAnimated;
                }

                if (Math.Abs(scale.Y - _scale.Y) > epsilon)
                {
                    labelScalingY.ForeColor = ColorIsAnimated;
                    textBoxScaleY.ForeColor = ColorIsAnimated;
                }

                if (Math.Abs(scale.Z - _scale.Z) > epsilon)
                {
                    labelScalingZ.ForeColor = ColorIsAnimated;
                    textBoxScaleZ.ForeColor = ColorIsAnimated;
                }

                if (Math.Abs(trans.X - _trans.X) > epsilon)
                {
                    labelTranslationX.ForeColor = ColorIsAnimated;
                    textBoxTransX.ForeColor = ColorIsAnimated;
                }

                if (Math.Abs(trans.Y - _trans.Y) > epsilon)
                {
                    labelTranslationY.ForeColor = ColorIsAnimated;
                    textBoxTransY.ForeColor = ColorIsAnimated;
                }

                if (Math.Abs(trans.Z - _trans.Z) > epsilon)
                {
                    labelTranslationZ.ForeColor = ColorIsAnimated;
                    textBoxTransZ.ForeColor = ColorIsAnimated;
                }
            }
            else
            {
                labelScalingX.ForeColor = ColorNotAnimated;
                textBoxScaleX.ForeColor = ColorNotAnimated;

                labelScalingY.ForeColor = ColorNotAnimated;
                textBoxScaleY.ForeColor = ColorNotAnimated;

                labelScalingZ.ForeColor = ColorNotAnimated;
                textBoxScaleZ.ForeColor = ColorNotAnimated;

                labelTranslationX.ForeColor = ColorNotAnimated;
                textBoxTransX.ForeColor = ColorNotAnimated;

                labelTranslationY.ForeColor = ColorNotAnimated;
                textBoxTransY.ForeColor = ColorNotAnimated;

                labelTranslationZ.ForeColor = ColorNotAnimated;
                textBoxTransZ.ForeColor = ColorNotAnimated;

                _scale = scale;
                _trans = trans;
                _rot = rot;
            }

            // rotation - more complicated because the display mode can be changed 
            _rotCurrent = rot;
            SetRotation(diffAgainstBaseMatrix);
        }


        protected Color ColorNotAnimated
        {
            get { return Color.Black; }
        }

        protected Color ColorIsAnimated
        {
            get { return Color.Red; }
        }


        private void SetRotation(bool diffAgainstBaseMatrix)
        {
            switch ((RotationMode)comboBoxRotMode.SelectedIndex)
            {
                case RotationMode.EulerXyzDegrees:
                case RotationMode.EulerXyzRadians:
                    labelRotationW.Visible = false;
                    textBoxRotW.Visible = false;

                    // TODO
                    break;

                case RotationMode.Quaternion:
                    labelRotationW.Visible = true;
                    textBoxRotW.Visible = true;

                    textBoxRotX.Text = _rotCurrent.X.ToString(Format);
                    textBoxRotY.Text = _rotCurrent.Y.ToString(Format);
                    textBoxRotZ.Text = _rotCurrent.Z.ToString(Format);
                    textBoxRotW.Text = _rotCurrent.W.ToString(Format);

                    if (diffAgainstBaseMatrix)
                    {
                        const double epsilon = 1e-5f;
                        if (Math.Abs(_rotCurrent.X - _rot.X) > epsilon)
                        {
                            labelRotationX.ForeColor = ColorIsAnimated;
                            textBoxRotX.ForeColor = ColorIsAnimated;
                        }
                        if (Math.Abs(_rotCurrent.Y - _rot.Y) > epsilon)
                        {
                            labelRotationY.ForeColor = ColorIsAnimated;
                            textBoxRotY.ForeColor = ColorIsAnimated;
                        }
                        if (Math.Abs(_rotCurrent.Z - _rot.Z) > epsilon)
                        {
                            labelRotationZ.ForeColor = ColorIsAnimated;
                            textBoxRotZ.ForeColor = ColorIsAnimated;
                        }
                        if (Math.Abs(_rotCurrent.W - _rot.W) > epsilon)
                        {
                            labelRotationW.ForeColor = ColorIsAnimated;
                            textBoxRotW.ForeColor = ColorIsAnimated;
                        }
                    }
                    
                    break;
                default:
                    Debug.Assert(false);
                    break;
            }

            if(!diffAgainstBaseMatrix)
            {
                textBoxRotX.ForeColor = ColorNotAnimated;
                textBoxRotY.ForeColor = ColorNotAnimated;
                textBoxRotZ.ForeColor = ColorNotAnimated;
                textBoxRotW.ForeColor = ColorNotAnimated;

                labelRotationX.ForeColor = ColorNotAnimated;
                labelRotationY.ForeColor = ColorNotAnimated;
                labelRotationZ.ForeColor = ColorNotAnimated;
                labelRotationW.ForeColor = ColorNotAnimated;
            }
        }
    }
}