// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

using System;
using System.Windows.Forms;

namespace FXPAL.DisplayCast.Player
{
    partial class Streamer
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Streamer));
            this.streamImage = new System.Windows.Forms.PictureBox();
            ((System.ComponentModel.ISupportInitialize)(this.streamImage)).BeginInit();
            this.SuspendLayout();
            // 
            // streamImage
            // 
            this.streamImage.BackColor = System.Drawing.Color.Black;
            this.streamImage.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.streamImage.Dock = System.Windows.Forms.DockStyle.Fill;
            this.streamImage.ErrorImage = null;
            this.streamImage.InitialImage = null;
            this.streamImage.Location = new System.Drawing.Point(0, 0);
            this.streamImage.Name = "streamImage";
            this.streamImage.Size = new System.Drawing.Size(803, 595);
            this.streamImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.streamImage.TabIndex = 0;
            this.streamImage.TabStop = false;
            this.streamImage.LocationChanged += new System.EventHandler(this.Streamer_SizeLocationChanged);
            this.streamImage.SizeChanged += new System.EventHandler(this.Streamer_SizeLocationChanged);
            // 
            // Streamer
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Inherit;
            this.AutoScroll = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.ClientSize = new System.Drawing.Size(803, 595);
            this.Controls.Add(this.streamImage);
            this.Cursor = System.Windows.Forms.Cursors.NoMove2D;
            this.DoubleBuffered = true;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MinimumSize = new System.Drawing.Size(64, 64);
            this.Name = "Streamer";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Show;
            this.Text = "FXPal DisplayCast Player";
            this.WindowState = System.Windows.Forms.FormWindowState.Minimized;
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.Streamer_Closed);
            this.Load += new System.EventHandler(this.Streamer_Load);
            this.LocationChanged += new System.EventHandler(this.Streamer_SizeLocationChanged);
            ((System.ComponentModel.ISupportInitialize)(this.streamImage)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        public PictureBox streamImage;
    }
}