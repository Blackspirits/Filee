# Linha de comandos em português de Portugal

O comando `filee` usa as predefinições e os motores da aplicação. O instalador ou o `install.sh` adiciona-o
ao sistema. A aplicação não precisa de estar aberta para converter ficheiros pela linha de comandos.

## Escolher o idioma

Por omissão, a CLI usa o idioma guardado no Filee. Se a aplicação estiver em modo automático, segue o idioma
de apresentação do sistema. Podes escolher um idioma apenas para o comando atual, sem alterar as definições:

```text
filee --language pt-PT help
filee --language en help
filee --language auto help
```

A CLI está traduzida em inglês e português de Portugal. Os outros idiomas usam inglês. Uma seleção pt-BR
não é substituída por pt-PT; enquanto não houver uma tradução própria da CLI para o Brasil, usa inglês.

Podes colocar `--language` antes do comando ou entre as suas opções:

```text
filee convert --help --language pt-PT
filee help watch --language pt-PT
```

A ajuda, a versão e os erros de argumentos não carregam motores nem alteram os ficheiros de definições.

## Converter ficheiros

```text
filee convert photo.heic --to jpg --language pt-PT
filee convert *.png --to webp --quality 80 -o converted --language pt-PT
filee convert D:\Scans --recursive --preset to-pdf --language pt-PT
```

Os nomes dos comandos e das opções mantêm-se: `convert`, `--to`, `--preset`, `--output`, entre outros.
Na ajuda, `<ficheiros>`, `<pasta>` e `<formato>` são indicações do que deves fornecer, não texto para copiar
literalmente. Por exemplo, substitui `<formato>` por `jpg`. Usa aspas nos caminhos com espaços.

Com `--preset`, podes indicar o ID, o nome traduzido ou o nome inglês de uma predefinição de origem.
Os nomes personalizados mantêm-se como os guardaste. Prefere os IDs em scripts, pois são estáveis:

```text
filee presets --language pt-PT
filee convert photo.png --preset grayscale --language pt-PT
filee formats heic --language pt-PT
```

Os padrões de nomes de ficheiro e os seus elementos, como `{name}` e `{preset}`, não são traduzidos.
Na CLI, `{preset}` mantém o nome inglês que já usava, mesmo com mensagens em português. Os nomes de
predefinições personalizados continuam a ser usados tal como foram guardados.

## Monitorizar uma pasta

```text
filee watch D:\Inbox --to pdf --move-originals --language pt-PT
```

Prime Ctrl+C para parar. Sem `--output`, os resultados ficam na subpasta `converted`.
Com `--move-originals`, os originais convertidos passam para `originals`. Estes nomes de pastas são literais
e não mudam com o idioma. `--recursive` inclui as subpastas.

Para ajustar o tempo durante o qual o ficheiro tem de ficar inalterado, usa `--settle`, com ponto decimal:

```text
filee watch D:\Inbox --to jpg --settle 1.5 --language pt-PT
```

A ajuda de `watch` mostra o valor por omissão da plataforma: 2 segundos no Windows e 5 no Linux/macOS.

## Scripts e resultados JSON

```text
filee convert photo.png --to webp --json --language pt-PT
```

Com `--json`, os nomes dos campos, estados, nomes de predefinições de origem e mensagens descritivas do
resultado mantêm-se em inglês, para preservar a compatibilidade com scripts. Nomes personalizados, caminhos
e detalhes devolvidos pelo sistema ou por motores externos mantêm-se tal como foram recebidos.
As mensagens de erro enviadas para stderr seguem o idioma escolhido. `--quiet` suprime a saída normal;
as falhas de conversão continuam a ser indicadas pelo código de saída.

Os códigos de saída não dependem do idioma:

| Código | Significado |
| --- | --- |
| `0` | A operação terminou com sucesso |
| `1` | Houve falhas ou ficheiros cancelados |
| `2` | Argumentos incorretos, entrada em falta ou configuração inválida |
| `3` | Não há ficheiros para converter |

Os detalhes técnicos de um erro podem vir diretamente do sistema ou do motor de conversão e estar noutro
idioma. A escolha do idioma das mensagens não altera a região usada para apresentar números nem a sintaxe
dos argumentos numéricos.
