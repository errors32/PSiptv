# PSiptv 1.6.9

## Sincronização Bluetooth por marcação de conteúdos

- A sincronização automática ocorre quando um podcast é marcado como ouvido/por ouvir ou um filme/episódio como concluído/por ver, incluindo a conclusão pelo leitor integrado.
- Removidas as consultas periódicas e as tentativas por temporizador. Posição de reprodução, visitas a podcasts, favoritos e início de reprodução não provocam ligações.
- Marcações próximas são agrupadas. Alterações pendentes são guardadas por lista/perfil e recuperadas na próxima marcação, reabertura da aplicação ou ativação do rádio Bluetooth.
- A troca inclui os estados vistos de filmes e episódios, mesmo sem os ter nos favoritos. Marcações «por ver» também se propagam; alterações independentes são preservadas e cópias antigas não repõem marcações removidas.
- O protocolo confirma a receção dos dados finais antes de fechar a ligação. A comunicação Bluetooth decorre fora da interface da aplicação.
- A página conserva o último erro, distinguindo versões incompatíveis, lista/perfil diferente, ligação interrompida e armazenamento indisponível.
- A página inicial, a biblioteca e o detalhe do estado atualizam-se quando chegam marcações do outro equipamento.

## Instalação

Atualize ambos os equipamentos para a versão 1.6.9. O protocolo Bluetooth v2 exige versões compatíveis nos dois lados. Emparelhe os equipamentos no Android, ative a sincronização em ambos, abra a mesma lista com as mesmas credenciais e nome de perfil e escolha o equipamento uma vez. A sincronização corre em segundo plano enquanto ambas as aplicações estão abertas. Os downloads de áudio não são transferidos.

## Validação

- 276 verificações automatizadas: 214 do núcleo, 38 regressões e 24 verificações de sincronização por consumo.
- Verificados ausência de consultas em repouso, agrupamento de eventos, marcações repetidas, cancelamento, convergência dos estados, confirmação de receção e erros específicos de perfil, versão e armazenamento.
- A comunicação do protocolo é testada com streams locais; a ligação Bluetooth entre tablet e telefone necessita de validação nos equipamentos reais.
- Android e Windows compilados em Release sem erros; permanecem os avisos existentes.
- APK assinado com a mesma identidade da versão 1.6.8. Pacote, assinatura, alinhamento e código de versão verificados.
- Código de versão Android: **298661731**.
- SHA-256: `EDD15EFCFBB29BBA976B86E9FD07A827AA7F420F8B8B9B1829DC552BAAFC7509`.
