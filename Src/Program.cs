using System;
using System.Linq;
using System.Windows.Forms;
using RT.Util.Dialogs;
using RT.Util.ExtensionMethods;

namespace i4c
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            MainForm form = new MainForm();

            if (args.Length == 0)
            {
                Application.Run(form);
            }
            else
            {
                if (args[0] == "?")
                {
                    DlgMessage.ShowInfo("Available tasks:\n\n" + "".Join(form.Tasks.Select(task => "* " + task.Name + "\n")));
                    return;
                }
                else
                {
                    Task task = form.Tasks.Where(t => t.Name.ToLower() == args[0]).First();
                    Form waitform = new Form();
                    waitform.Text = "i4c - please wait...";
                    waitform.Height = 50;
                    waitform.Show();
                    task.Process(args.Skip(1).ToArray());
                    waitform.Close();
                }
            }
        }
    }
}
