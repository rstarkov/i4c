using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RT.Util;
using RT.Util.Forms;

namespace i4c
{
    [Settings("i4c", SettingsKind.UserSpecific)]
    class Settings : SettingsBase
    {
        public ManagedForm.Settings MainFormSettings = new ManagedForm.Settings();
        public Dictionary<string, string> LastArgs = new Dictionary<string, string>();
    }
}
