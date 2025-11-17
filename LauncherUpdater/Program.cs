using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace LauncherUpdater
{
    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // ---------------------------------------------------------------
                // LAUNCHER UPDATER - O Operário que faz a troca dos arquivos
                // ---------------------------------------------------------------

                if (args.Length < 4) return;

                // Parse simplificado dos argumentos
                var argsDict = args.Select(s => s.Split(new[] { '=' }, 2))
                                   .Where(p => p.Length == 2)
                                   .ToDictionary(a => a[0], a => a[1]);

                int parentPid = int.Parse(argsDict["--parent-pid"]);
                string sourceDir = argsDict["--source-dir"]; // Pasta Temp (Origem)
                string targetDir = argsDict["--target-dir"]; // Pasta Instalação (Destino)
                string exeName = argsDict["--exe-name"];     // Nome do executável principal (PDV_Launcher.exe)

                // 1. Espera o Launcher principal (Pai) morrer
                try
                {
                    Process parent = Process.GetProcessById(parentPid);
                    parent.WaitForExit(10000); // Espera até 10s
                }
                catch { /* Já morreu, segue o jogo */ }

                Thread.Sleep(1000); // Respiro para o SO liberar o arquivo

                // 2. Copia TUDO da pasta Temp para a Pasta Real (Sobrescrevendo)
                CopyDirectory(sourceDir, targetDir);

                // 3. Reinicia o Launcher novo
                string newExePath = Path.Combine(targetDir, exeName);

                // Se o nome do executável na pasta de destino estiver errado (ex: versão antiga com typo),
                // o updater tenta iniciar o nome correto "PDV_Launcher.exe" se ele foi copiado agora.
                if (!File.Exists(newExePath) && exeName.Contains("Laucher"))
                {
                    // Tentativa de autocorreção de nome se a versão antiga tinha typo
                    string fixedName = exeName.Replace("Laucher", "Launcher");
                    string fixedPath = Path.Combine(targetDir, fixedName);
                    if (File.Exists(fixedPath)) newExePath = fixedPath;
                }

                if (File.Exists(newExePath))
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo(newExePath)
                    {
                        WorkingDirectory = targetDir,
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                }
            }
            catch (Exception ex)
            {
                // Se der pau aqui, não tem UI. Grava um txt na pasta temp ou desktop.
                string logFile = Path.Combine(Path.GetTempPath(), "pdv_updater_error.log");
                File.WriteAllText(logFile, ex.ToString());
            }
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(targetDir, new DirectoryInfo(subDir).Name);
                CopyDirectory(subDir, destSubDir);
            }
        }
    }
}