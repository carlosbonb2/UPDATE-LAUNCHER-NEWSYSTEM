using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace LauncherUpdater
{
    internal class Program
    {
        // Nome do executável principal para reabrir no final
        static string _exeNameSolicitado = "PDV_Launcher.exe";
        static string _logPath = "";

        static void Main(string[] args)
        {
            try
            {
                // Define local de log temporário até sabermos o diretório final
                _logPath = Path.Combine(Path.GetTempPath(), "updater_init_log.txt");
                Log("Iniciando Updater...");

                if (args.Contains("--fix-mode"))
                {
                    ExecutarCorrecaoNome(args);
                    return;
                }

                if (args.Length > 0)
                {
                    ExecutarAtualizacaoPadrao(args);
                }
            }
            catch (Exception ex)
            {
                Log($"ERRO FATAL NO MAIN: {ex}");
            }
        }

        private static void ExecutarAtualizacaoPadrao(string[] args)
        {
            int parentPid = int.Parse(GetArg(args, "--pid"));
            string zipPath = GetArg(args, "--zip-path");
            string targetDir = GetArg(args, "--target-dir");
            _exeNameSolicitado = GetArg(args, "--exe-name");

            // Agora que temos o targetDir, movemos o log para lá para ficar persistente
            if (!string.IsNullOrEmpty(targetDir))
            {
                _logPath = Path.Combine(targetDir, "updater_last_run.log");
                if (File.Exists(_logPath)) File.Delete(_logPath); // Limpa log antigo
            }

            Log($"Diretório Alvo: {targetDir}");
            Log($"Arquivo ZIP: {zipPath}");

            // 1. Garantir que o Launcher (e processos filhos) morreram
            Log("Aguardando fechamento do Launcher...");
            GarantirProcessoMorto(parentPid);
            ForcarLimpezaDeProcessosTravados(targetDir);

            // 2. Prepara extração
            string tempExtractDir = Path.Combine(Path.GetTempPath(), "PDV_Update_" + Guid.NewGuid().ToString().Substring(0, 8));
            Log($"Extraindo para temporário: {tempExtractDir}");

            try
            {
                if (File.Exists(zipPath))
                {
                    Directory.CreateDirectory(tempExtractDir);
                    ZipFile.ExtractToDirectory(zipPath, tempExtractDir, true);

                    // 3. Copia os arquivos (Sobrescrevendo)
                    Log("Iniciando cópia de arquivos...");
                    CopyDirectory(tempExtractDir, targetDir);
                    Log("Cópia finalizada com sucesso.");
                }
                else
                {
                    Log("ERRO: Arquivo ZIP não encontrado na origem.");
                }
            }
            catch (Exception ex)
            {
                Log($"ERRO DURANTE ATUALIZAÇÃO: {ex}");
                throw; // Relança para cair no catch do Main se necessário
            }
            finally
            {
                // Limpeza
                Log("Limpando arquivos temporários...");
                try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }
                try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            }

            // 4. Reabre o Launcher
            string exeParaIniciar = Path.Combine(targetDir, _exeNameSolicitado);
            Log($"Tentando iniciar: {exeParaIniciar}");

            if (File.Exists(exeParaIniciar))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(exeParaIniciar) { WorkingDirectory = targetDir, UseShellExecute = true });
                    Log("Launcher reiniciado.");
                }
                catch (Exception ex)
                {
                    Log($"Falha ao reiniciar launcher: {ex.Message}");
                }
            }
        }

        private static void ExecutarCorrecaoNome(string[] args)
        {
            // Mantive sua lógica original aqui, apenas adicionando Log
            int pid = int.Parse(GetArg(args, "--pid"));
            string dir = GetArg(args, "--dir");
            string oldName = GetArg(args, "--old");
            string newName = GetArg(args, "--new");
            string pathOld = Path.Combine(dir, oldName);
            string pathNew = Path.Combine(dir, newName);

            _logPath = Path.Combine(dir, "updater_fix_log.txt");

            try
            {
                if (pid > 0) Process.GetProcessById(pid).WaitForExit(5000);
            }
            catch { }

            Thread.Sleep(1000);

            if (File.Exists(pathOld))
            {
                if (File.Exists(pathNew)) try { File.Delete(pathNew); } catch { }
                File.Move(pathOld, pathNew);
                Log($"Renomeado de {oldName} para {newName}");
            }

            CorrigirAtalho("PDV NewSystem", pathNew, dir);

            if (File.Exists(pathNew))
                Process.Start(new ProcessStartInfo(pathNew) { WorkingDirectory = dir, UseShellExecute = true });
        }

        // --- MÉTODOS AUXILIARES ROBUSTOS ---

        private static void Log(string msg)
        {
            try
            {
                string logLine = $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}";
                File.AppendAllText(_logPath, logLine);
                Console.WriteLine(msg); // Para debug se rodar via cmd
            }
            catch { /* Se falhar log, paciência */ }
        }

        private static void GarantirProcessoMorto(int pid)
        {
            if (pid <= 0) return;
            try
            {
                Process p = Process.GetProcessById(pid);
                p.WaitForExit(5000); // Espera 5s educadamente
                if (!p.HasExited)
                {
                    Log($"Processo {pid} ainda ativo. Forçando encerramento...");
                    p.Kill(); // Mata sem piedade
                }
            }
            catch { /* Processo já não existe */ }
        }

        private static void ForcarLimpezaDeProcessosTravados(string targetDir)
        {
            // Procura qualquer processo rodando a partir do diretório de instalação
            // Isso evita erro de "Arquivo em uso" ao tentar copiar o executável principal
            try
            {
                var processes = Process.GetProcesses()
                                       .Where(p =>
                                       {
                                           try { return p.MainModule != null && p.MainModule.FileName.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase); }
                                           catch { return false; }
                                       });

                foreach (var p in processes)
                {
                    if (p.Id != Process.GetCurrentProcess().Id) // Não mata a si mesmo
                    {
                        Log($"Matando processo zumbi na pasta alvo: {p.ProcessName} ({p.Id})");
                        try { p.Kill(); } catch { }
                    }
                }
            }
            catch (Exception ex) { Log($"Aviso na limpeza de processos: {ex.Message}"); }
        }

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
            // Sua lógica original de atalho (PowerShell)
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

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));

                // Tenta copiar com retries (Exponential Backoff)
                bool copiou = false;
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        File.Copy(file, destFile, true);
                        copiou = true;
                        break;
                    }
                    catch (IOException)
                    {
                        Log($"Arquivo bloqueado: {Path.GetFileName(file)}. Tentativa {i + 1}/5...");
                        Thread.Sleep(500 * (i + 1)); // Espera 500ms, 1s, 1.5s...
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Log($"Erro de permissão em: {Path.GetFileName(file)}. Tentando remover Atributo ReadOnly...");
                        try { File.SetAttributes(destFile, FileAttributes.Normal); } catch { }
                        Thread.Sleep(200);
                    }
                }

                if (!copiou) Log($"ERRO CRÍTICO: Não foi possível copiar {Path.GetFileName(file)} após 5 tentativas.");
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(targetDir, new DirectoryInfo(subDir).Name);
                CopyDirectory(subDir, destSubDir);
            }
        }
    }
}