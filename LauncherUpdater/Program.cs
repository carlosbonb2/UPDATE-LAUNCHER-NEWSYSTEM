using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace LauncherUpdater
{
    internal class Program
    {
        // CONSTANTES
        const string NOME_ATALHO = "PDV NewSystem";

        static void Main(string[] args)
        {
            try
            {
                // MODO 1: Correção de Nome (Migração v1 -> v2)
                // args: --fix-mode --pid=1234 --dir="C:\Path" --old="PDV_Laucher.exe" --new="PDV_Launcher.exe"
                if (args.Contains("--fix-mode"))
                {
                    ExecutarCorrecaoNome(args);
                    return;
                }

                // MODO 2: Atualização Padrão (Cópia de arquivos)
                if (args.Length < 4) return;
                ExecutarAtualizacaoPadrao(args);
            }
            catch (Exception ex)
            {
                string log = Path.Combine(Path.GetTempPath(), "updater_error.log");
                File.WriteAllText(log, $"{DateTime.Now}: {ex}");
            }
        }

        private static void ExecutarAtualizacaoPadrao(string[] args)
        {
            int parentPid = int.Parse(GetArg(args, "--parent-pid"));
            string sourceDir = GetArg(args, "--source-dir");
            string targetDir = GetArg(args, "--target-dir");
            string exeNameSolicitado = GetArg(args, "--exe-name");

            // 1. Espera o Launcher morrer
            try
            {
                if (parentPid > 0)
                {
                    Process parent = Process.GetProcessById(parentPid);
                    parent.WaitForExit(10000);
                }
            }
            catch { }

            Thread.Sleep(1000);

            // 2. Atualiza os arquivos
            CopyDirectory(sourceDir, targetDir);

            // 3. Iniciar o Launcher
            string exeParaIniciar = Path.Combine(targetDir, exeNameSolicitado);
            if (File.Exists(exeParaIniciar))
            {
                Process.Start(new ProcessStartInfo(exeParaIniciar) { WorkingDirectory = targetDir, UseShellExecute = true });
            }
        }

        private static void ExecutarCorrecaoNome(string[] args)
        {
            int pid = int.Parse(GetArg(args, "--pid"));
            string dir = GetArg(args, "--dir");
            string oldName = GetArg(args, "--old");
            string newName = GetArg(args, "--new");

            string pathOld = Path.Combine(dir, oldName);
            string pathNew = Path.Combine(dir, newName);

            // 1. Espera o Launcher (errado) fechar
            try
            {
                if (pid > 0)
                {
                    Process p = Process.GetProcessById(pid);
                    p.WaitForExit(5000);
                }
            }
            catch { }

            Thread.Sleep(1000);

            // 2. Renomeia o arquivo (A Mágica)
            if (File.Exists(pathOld))
            {
                if (File.Exists(pathNew)) try { File.Delete(pathNew); } catch { } // Limpa se já existir lixo
                File.Move(pathOld, pathNew);
            }

            // 3. Corrige o Atalho (Importantíssimo)
            CorrigirAtalho(NOME_ATALHO, pathNew, dir);

            // 4. Inicia o Launcher com o nome certo
            if (File.Exists(pathNew))
            {
                Process.Start(new ProcessStartInfo(pathNew) { WorkingDirectory = dir, UseShellExecute = true });
            }
        }

        private static string GetArg(string[] args, string key)
        {
            foreach (var arg in args)
            {
                // Suporta --key=value e key=value
                string cleanArg = arg.TrimStart('-');
                string cleanKey = key.TrimStart('-');
                if (cleanArg.StartsWith(cleanKey + "="))
                    return cleanArg.Split(new[] { '=' }, 2)[1].Trim('"');
            }
            return "";
        }

        private static void CorrigirAtalho(string nome, string target, string workDir)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string link = Path.Combine(desktop, nome + ".lnk");
                if (File.Exists(link)) File.Delete(link);

                // Cria o novo via PowerShell (rápido e nativo)
                string ps = $"-NoProfile -Command \"$s=(New-Object -Com WScript.Shell).CreateShortcut('{link}');$s.TargetPath='{target}';$s.WorkingDirectory='{workDir}';$s.Save()\"";

                Process.Start(new ProcessStartInfo("powershell", ps)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                })?.WaitForExit();
            }
            catch { }
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                try { File.Copy(file, destFile, true); } catch { Thread.Sleep(200); try { File.Copy(file, destFile, true); } catch { } }
            }
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(targetDir, new DirectoryInfo(subDir).Name);
                CopyDirectory(subDir, destSubDir);
            }
        }
    }
}