namespace i4c
{
    partial class MainForm
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
            if (disposing && (components != null))
            {
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
            this.MainMenu = new System.Windows.Forms.MenuStrip();
            this.miAction = new System.Windows.Forms.ToolStripMenuItem();
            this.miActionA1 = new System.Windows.Forms.ToolStripMenuItem();
            this.pnl = new RT.Util.Controls.DoubleBufferedPanel();
            this.miActionA2 = new System.Windows.Forms.ToolStripMenuItem();
            this.MainMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // MainMenu
            // 
            this.MainMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.miAction});
            this.MainMenu.Location = new System.Drawing.Point(0, 0);
            this.MainMenu.Name = "MainMenu";
            this.MainMenu.Size = new System.Drawing.Size(284, 24);
            this.MainMenu.TabIndex = 0;
            this.MainMenu.Text = "menuStrip1";
            // 
            // miAction
            // 
            this.miAction.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.miActionA1,
            this.miActionA2});
            this.miAction.Name = "miAction";
            this.miAction.Size = new System.Drawing.Size(54, 20);
            this.miAction.Text = "Action";
            // 
            // miActionA1
            // 
            this.miActionA1.Name = "miActionA1";
            this.miActionA1.ShortcutKeys = System.Windows.Forms.Keys.F1;
            this.miActionA1.Size = new System.Drawing.Size(152, 22);
            this.miActionA1.Text = "A1";
            this.miActionA1.Click += new System.EventHandler(this.miActionA1_Click);
            // 
            // pnl
            // 
            this.pnl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnl.Location = new System.Drawing.Point(0, 24);
            this.pnl.Name = "pnl";
            this.pnl.Size = new System.Drawing.Size(284, 240);
            this.pnl.TabIndex = 1;
            this.pnl.PaintBuffer += new System.Windows.Forms.PaintEventHandler(this.pnl_PaintBuffer);
            // 
            // miActionA2
            // 
            this.miActionA2.Name = "miActionA2";
            this.miActionA2.ShortcutKeys = System.Windows.Forms.Keys.F2;
            this.miActionA2.Size = new System.Drawing.Size(152, 22);
            this.miActionA2.Text = "A2";
            this.miActionA2.Click += new System.EventHandler(this.miActionA2_Click);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(284, 264);
            this.Controls.Add(this.pnl);
            this.Controls.Add(this.MainMenu);
            this.MainMenuStrip = this.MainMenu;
            this.Name = "MainForm";
            this.Text = "i4c";
            this.WindowState = System.Windows.Forms.FormWindowState.Maximized;
            this.MainMenu.ResumeLayout(false);
            this.MainMenu.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip MainMenu;
        private System.Windows.Forms.ToolStripMenuItem miAction;
        private System.Windows.Forms.ToolStripMenuItem miActionA1;
        private RT.Util.Controls.DoubleBufferedPanel pnl;
        private System.Windows.Forms.ToolStripMenuItem miActionA2;
    }
}

