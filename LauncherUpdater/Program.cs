using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression; // Necessário para descompactar o ZIP
using System.Linq;
using System.Threading;

namespace LauncherUpdater
{
    internal class Program
    {
        const string NOME_ATALHO = "PDV NewSystem";

        static void Main(string[] args)
        {
            try
            {
                // Se tiver o argumento de correção, vai para o modo de renomear
                if (args.Contains("--fix-mode"))
                {
                    ExecutarCorrecaoNome(args);
                    return;
                }

                // Se não, e tiver argumentos, é atualização padrão
                if (args.Length > 0)
                {
                    ExecutarAtualizacaoPadrao(args);
                }
            }
            catch (Exception ex)
            {
                // Log de emergência caso o Updater morra
                string log = Path.Combine(Path.GetTempPath(), "updater_crash.log");
                File.WriteAllText(log, $"{DateTime.Now}: {ex}");
            }
        }

        private static void ExecutarAtualizacaoPadrao(string[] args)
        {
            int parentPid = int.Parse(GetArg(args, "--pid"));
            string zipPath = GetArg(args, "--zip-path"); // Caminho do arquivo .zip baixado
            string targetDir = GetArg(args, "--target-dir"); // Pasta onde o PDV está instalado (Pasta App ou Launcher)
            string exeNameSolicitado = GetArg(args, "--exe-name");

            // 1. Espera o processo pai fechar (Launcher ou PDV)
            if (parentPid > 0)
            {
                try
                {
                    Process parent = Process.GetProcessById(parentPid);
                    parent.WaitForExit(10000); // Espera até 10s
                }
                catch { /* Processo já morreu */ }
            }
            Thread.Sleep(1000); // Respira fundo

            string tempExtractDir = string.Empty;

            try
            {
                if (File.Exists(zipPath))
                {
                    tempExtractDir = Path.Combine(Path.GetTempPath(), "PDV_Extracted_" + Guid.NewGuid().ToString().Substring(0, 8));

                    // Cria pasta temporária e extrai o ZIP lá dentro
                    Directory.CreateDirectory(tempExtractDir);
                    ZipFile.ExtractToDirectory(zipPath, tempExtractDir, true);

                    // 2. Copia os arquivos extraídos (incluindo local_version.txt) para a pasta de instalação (Sobrescrevendo)
                    // Esta função usa uma lógica de 3 tentativas para contornar problemas de bloqueio de arquivo.
                    CopyDirectory(tempExtractDir, targetDir);
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "updater_copy_error.txt"), ex.ToString());
                throw;
            }
            finally
            {
                // Limpeza: Apaga a pasta temporária e o ZIP
                try { if (!string.IsNullOrEmpty(tempExtractDir) && Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }
                try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            }

            // 3. Reabre o executável solicitado (PDV ou Launcher)
            string exeParaIniciar = Path.Combine(targetDir, exeNameSolicitado);
            if (File.Exists(exeParaIniciar))
            {
                // Inicia o processo de forma oculta para uma atualização limpa (silenciosa)
                Process.Start(new ProcessStartInfo(exeParaIniciar)
                {
                    WorkingDirectory = targetDir,
                    UseShellExecute = true,
                    CreateNoWindow = true, // Tenta não criar janela
                    WindowStyle = ProcessWindowStyle.Hidden // Tenta esconder
                });
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

            try { if (pid > 0) Process.GetProcessById(pid).WaitForExit(5000); } catch { }
            Thread.Sleep(1000);

            // Renomeia o executável
            if (File.Exists(pathOld))
            {
                if (File.Exists(pathNew)) try { File.Delete(pathNew); } catch { }
                File.Move(pathOld, pathNew);
            }

            // Corrige o atalho na área de trabalho
            CorrigirAtalho(NOME_ATALHO, pathNew, dir);

            // Abre o novo
            if (File.Exists(pathNew))
                Process.Start(new ProcessStartInfo(pathNew) { WorkingDirectory = dir, UseShellExecute = true });
        }

        // --- MÉTODOS AUXILIARES ---

        private static string GetArg(string[] args, string key)
        {
            foreach (var arg in args)
            {
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
                string ps = $"-NoProfile -Command \"$s=(New-Object -Com WScript.Shell).CreateShortcut('{link}');$s.TargetPath='{target}';$s.WorkingDirectory='{workDir}';$s.IconLocation='{target},0';$s.Description='PDV NewSystem';$s.Save()\"";
                Process.Start(new ProcessStartInfo("powershell", ps) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden })?.WaitForExit();
            }
            catch { }
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            // Copia Arquivos
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                // Tenta copiar 3 vezes (caso o arquivo esteja travado brevemente)
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        File.Copy(file, destFile, true);
                        break;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(500);
                    }
                }
            }

            // Copia Subpastas (Recursivo)
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(targetDir, new DirectoryInfo(subDir).Name);
                CopyDirectory(subDir, destSubDir);
            }
        }
    }
}