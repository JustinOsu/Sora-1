using System.Collections.Generic;
using Sora.Framework;
using Sora.Framework.Objects;

namespace Sora.Bot
{
    public interface ISoraCommand
    {
        string Command { get; }
        string Description { get; }
        List<Argument> Args { get; }
        int ExpectedArgs { get; }
        Permission RequiredPermission { get; }

        bool Execute(Presence executer, string[] args);
    }
}
