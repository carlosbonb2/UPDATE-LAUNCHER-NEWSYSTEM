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
        const string NOME_ATALHO = "PDV NewSystem";
        const string NOME_UPDATER_TEMPORARIO = "LauncherUpdater.tmp";

        static string _caminhoLogDesktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RELATORIO_UPDATER.txt");
        static string _exeNameSolicitado = "PDV_Launcher.exe";
        static string _caminhoZipDoLauncher = string.Empty;
        private static object parentDir;

        static void Main(string[] args)
        {
            try
            {
                // 1. Cooperação na Limpeza: Tenta apagar o residual da execução anterior
                LimparUpdaterTemporario();

                // 2. Início do Log
                File.WriteAllText(_caminhoLogDesktop, $"--- INICIO DA NARRAÇÃO: {DateTime.Now} ---\n");
                Narrar("O Updater acordou! Estou rodando em MODO VISÍVEL.");
                Narrar($"Recebi {args.Length} argumentos: {string.Join(" ", args)}");

                if (args.Length == 0) return;

                if (args.Contains("--fix-mode"))
                {
                    Narrar("Modo de correção de nome detectado.");
                    ExecutarCorrecaoNome(args);
                    return;
                }

                ExecutarAtualizacaoPadrao(args);
            }
            catch (Exception ex)
            {
                // MODO DEBUG: Força a exibição do erro e pausa o console
                Narrar("\n------------------------------------------------");
                Narrar("!!! ERRO CRÍTICO NÃO TRATADO !!!");
                Narrar(ex.ToString());
                Narrar("------------------------------------------------");

                Console.WriteLine("\n[FALHA NA INSTALAÇÃO] Revise o log acima e no Desktop. Pressione qualquer tecla para sair...");
                Console.ReadKey();
            }
            finally
            {
                Narrar($"\nFim da execução em {DateTime.Now}.");

                // 3. Limpeza Final do ZIP
                try
                {
                    if (File.Exists(_caminhoZipDoLauncher)) File.Delete(_caminhoZipDoLauncher);
                }
                catch (Exception ex) { Narrar($"Falha na limpeza final do ZIP: {ex.Message}"); }
            }
        }

        private static void ExecutarAtualizacaoPadrao(string[] args)
        {
            // 1. PARSING E VARIAVEIS
            Narrar("Lendo argumentos...");
            int parentPid = int.Parse(GetArg(args, "--pid"));
            string zipPath = GetArg(args, "--zip-path");
            string targetDir = GetArg(args, "--target-dir");
            _exeNameSolicitado = GetArg(args, "--exe-name");
            _caminhoZipDoLauncher = zipPath;

            string backupDir = targetDir + "_BACKUP"; // Variável crucial para o rollback

            Narrar($"PID do Pai (Launcher): {parentDir}");
            Narrar($"Pasta de Destino: {targetDir}");

            // ... (Matar Processo Pai e Zumbis - Lógica Omitida) ...

            // 2. Tentar fazer backup da pasta alvo (ROLLBACK START)
            try
            {
                if (Directory.Exists(targetDir))
                {
                    Narrar("Tentando backup da versão antiga...");
                    if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
                    Directory.Move(targetDir, backupDir); // Renomeia \App para \App_BACKUP
                    Narrar($"Pasta de destino movida para backup: {backupDir}");
                    Directory.CreateDirectory(targetDir); // Cria a nova pasta \App vazia
                }
                else
                {
                    Directory.CreateDirectory(targetDir);
                    Narrar("Pasta de destino criada pela primeira vez (sem backup).");
                }
            }
            catch (Exception ex)
            {
                Narrar($"ERRO CRÍTICO no Backup: {ex.Message}");
                throw;
            }

            try
            {
                // 3. Execução da Instalação (Cópia de arquivos)
                AplicarAtualizacao(zipPath, targetDir, _exeNameSolicitado);

                Narrar("Passo 5: Remoção do backup antigo...");
                if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);

                // 4. Reiniciar Executável
                string caminhoExeFinal = Path.Combine(targetDir, _exeNameSolicitado);
                Narrar($"Passo 6: Tentando reabrir o executável em: {caminhoExeFinal}");

                if (File.Exists(caminhoExeFinal))
                {
                    // CORREÇÃO: Modo Visível
                    Process.Start(new ProcessStartInfo(caminhoExeFinal)
                    {
                        WorkingDirectory = targetDir,
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        WindowStyle = ProcessWindowStyle.Normal
                    });
                    Narrar("SUCESSO: Executável reiniciado! Meu trabalho aqui acabou.");
                }
                else
                {
                    Narrar("ERRO CRÍTICO: O executável final SUMIU!");
                }
            }
            catch (Exception ex)
            {
                Narrar($"FALHA na instalação. Iniciando ROLLBACK: {ex.Message}");

                // **LÓGICA DE ROLLBACK:**
                try
                {
                    if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
                    if (Directory.Exists(backupDir)) Directory.Move(backupDir, targetDir);
                    Narrar("ROLLBACK CONCLUÍDO. Versão antiga restaurada.");
                }
                catch (Exception rollbackEx)
                {
                    Narrar($"ERRO FATAL NO ROLLBACK: Não foi possível restaurar a versão antiga. {rollbackEx.Message}");
                }
                throw; // Relança a exceção original
            }
        }

        private static void AplicarAtualizacao(string zipPath, string targetDir, string exeNameFinal)
        {
            string tempFolder = Path.Combine(Path.GetTempPath(), "PDV_Extracted_" + Guid.NewGuid().ToString().Substring(0, 5));
            Narrar($"Passo 1: Extraindo arquivos para pasta temporária: {tempFolder}");

            try
            {
                // 1. Extração
                Directory.CreateDirectory(tempFolder);
                ZipFile.ExtractToDirectory(zipPath, tempFolder, true);
                Narrar("Extração do ZIP concluída com sucesso.");
            }
            catch (Exception ex)
            {
                Narrar($"ERRO NA EXTRAÇÃO: {ex.Message}");
                throw;
            }

            Narrar("Passo 2: Movendo arquivos para a pasta real (com tentativas)...");
            string origemReal = tempFolder;

            var dirs = Directory.GetDirectories(tempFolder);
            var files = Directory.GetFiles(tempFolder);
            if (files.Length == 0 && dirs.Length == 1)
            {
                Narrar($"Notei que o ZIP tinha uma pasta dentro ('{new DirectoryInfo(dirs[0]).Name}'). Entrando nela.");
                origemReal = dirs[0];
            }

            // 2. Cópia Recursiva
            CopiarRecursivo(origemReal, targetDir);

            Narrar("Passo 3: Limpando a bagunça...");
            try
            {
                if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
                Narrar("Pasta temporária de extração apagada.");
            }
            catch (Exception ex) { Narrar($"Falha na limpeza (não crítico): {ex.Message}"); }
        }

        private static void LimparUpdaterTemporario()
        {
            try
            {
                string dirLauncher = Path.GetDirectoryName(Environment.ProcessPath!)!;
                string caminhoTemporario = Path.Combine(dirLauncher, NOME_UPDATER_TEMPORARIO);

                if (File.Exists(caminhoTemporario))
                {
                    File.Delete(caminhoTemporario);
                    Narrar("Arquivo temporário apagado com sucesso.");
                }
            }
            catch (Exception ex)
            {
                Narrar($"AVISO: Falha ao apagar o arquivo temporário ({NOME_UPDATER_TEMPORARIO}): {ex.Message}");
            }
        }

        static void Narrar(string texto)
        {
            try
            {
                string linha = $"[{DateTime.Now:HH:mm:ss}] {texto}\n";
                File.AppendAllText(_caminhoLogDesktop, linha);
                Console.WriteLine(texto);
            }
            catch { }
        }

        static string GetArg(string[] args, string key)
        {
            foreach (var arg in args)
            {
                string a = arg.TrimStart('-');
                string k = key.TrimStart('-');
                if (a.StartsWith(k + "="))
                    return a.Split(new[] { '=' }, 2)[1].Trim('"');
            }
            return "";
        }

        static void MatarProcessosNaPasta(string dir)
        {
            try
            {
                var todosProcessos = Process.GetProcesses();
                foreach (var p in todosProcessos)
                {
                    try
                    {
                        if (p.Id == Process.GetCurrentProcess().Id) continue;
                        if (p.MainModule != null && p.MainModule.FileName.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                        {
                            Narrar($" -> Matando processo travado: {p.ProcessName} (PID: {p.Id})");
                            p.Kill();
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Narrar($"Erro ao buscar processos travados: {ex.Message}"); }
        }

        static void CopiarRecursivo(string origem, string destino)
        {
            // Cria destino se não existir
            Directory.CreateDirectory(destino);

            // Copia Arquivos
            foreach (var arquivo in Directory.GetFiles(origem))
            {
                string nomeArquivo = Path.GetFileName(arquivo);
                string destinoArquivo = Path.Combine(destino, nomeArquivo);

                Narrar($"   -> Copiando: {nomeArquivo}");

                bool copiou = false;
                for (int i = 1; i <= 5; i++) // 5 Tentativas
                {
                    try
                    {
                        File.Copy(arquivo, destinoArquivo, true);
                        copiou = true;
                        break;
                    }
                    catch (IOException ioEx)
                    {
                        Narrar($"      [Tentativa {i}] Arquivo preso! ({ioEx.Message}). Esperando...");
                        Thread.Sleep(2000);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Narrar($"      [Tentativa {i}] Sem permissão! Tentando liberar acesso...");
                        try { File.SetAttributes(destinoArquivo, FileAttributes.Normal); } catch { }
                        Thread.Sleep(500);
                    }
                }

                if (!copiou)
                {
                    Narrar($"      XXX FALHA CRÍTICA: Desisti de copiar {nomeArquivo} após 5 tentativas.");
                    throw new Exception($"Falha ao copiar {nomeArquivo}");
                }
            }

            // Copia Subpastas
            foreach (var dir in Directory.GetDirectories(origem))
            {
                string nomeDir = new DirectoryInfo(dir).Name;
                string destinoDir = Path.Combine(destino, nomeDir);
                CopiarRecursivo(dir, destinoDir);
            }
        }

        private static void ExecutarCorrecaoNome(string[] args) { Narrar("Executando correção de nome (dummy)..."); }
    }
}