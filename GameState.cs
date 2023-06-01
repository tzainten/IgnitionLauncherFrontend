using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IgnitionLauncherFrontend;

public enum GameState : int
{
    CanPlay = 0,
    Verifying,
    Outdated,
    NotInstalled
}