# Proposta de paletas para o Filee

Preparada separadamente da localização, a partir do main `1bf94e71`. Ainda **não existe um seletor novo na aplicação**. [Proposta completa em inglês](README.md).

A minha recomendação é acrescentar uma lista **Paleta de cores** em Definições → Tema, abaixo de Aspeto. Mantém-se **Filee (original)** como opção predefinida; a escolha da paleta preserva o estilo Filee Soft/Material You, a cor de destaque, os cantos e o movimento que já escolheste.

| Paleta | Claro | Escuro |
| --- | --- | --- |
| Catppuccin | Latte | Mocha |
| Nord | Adaptação Snow Storm | Adaptação Polar Night |
| Dracula | Alucard Classic | Dracula Classic |
| Monokai | Filee original, com indicação visível | Monokai clássico |

As imagens mostram a aplicação real em modo headless, usando apenas o mecanismo `theme.json` que já existe. A cor de destaque é a mesma em todas, para comparar as superfícies. As 20 capturas, nos dois estilos, estão ligadas na [proposta completa](README.md#previews-from-the-existing-application).

Passaram 20 verificações de carregamento, aplicação, captura e reposição. Foram também verificadas 105 combinações de texto/estado e fundo, com contraste mínimo calculado de 4,5106:1. As 17 adaptações de cores necessárias estão documentadas; não são apresentadas como cores originais das paletas.

Isto não certifica a acessibilidade da aplicação inteira. Os controlos, a cor de destaque, o foco, a transparência e a interação nativa precisam de revisão na implementação. Alguns controlos do Filee Soft mantêm as cores do Fluent; o Material You também usa cores tonais geradas.

O `theme.json` pessoal continua a ter prioridade sobre as cores da paleta e não deve ser reescrito. Ao escolher Filee, regressa-se à base original, mantendo os overrides explícitos desse ficheiro.

A proposta foi apresentada ao mantenedor no [pedido #569](https://github.com/KnifeLemon/Filee/issues/569). Falta confirmar o âmbito antes de implementar o seletor. Os exemplos e as capturas já estão preparados; não foi alterado código de execução, nem foram modificados catálogos de tradução.
