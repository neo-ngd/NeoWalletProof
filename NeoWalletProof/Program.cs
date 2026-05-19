using NeoWalletProof.Cli;
using NeoWalletProof.Configuration;
using NeoWalletProof.Shell;

AppBootstrap.Initialize();

if (args.Length > 0)
{
    var code = CliRunner.Run(args);
    if (code >= 0)
        return code;
}

new ProofConsole().Run();
return 0;
