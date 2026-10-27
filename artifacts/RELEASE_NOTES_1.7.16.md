# PSiptv 1.7.16 — Estado da ligação sem repetições

- As consultas periódicas de estado do relógio deixam de voltar a escrever
  **A ligar…** enquanto o comando já está ligado.
- Mensagens iguais não são reescritas no ecrã. O nome do equipamento, a categoria
  e os avisos continuam a atualizar quando o respetivo conteúdo muda.
- A procura e as tentativas automáticas de recuperação decorrem em silêncio.
  Uma falha mantém o aviso visível; a recuperação volta a mostrar o estado do
  equipamento. O Bluetooth tenta recuperar o telemóvel após uma ligação perdida.
- A preferência do equipamento Bluetooth é guardada quando a ligação se
  estabelece, em vez de ser regravada a cada consulta.
- Mantém o transporte Wear OS, a sincronização de podcasts e o splashscreen da
  versão anterior. Inclui os pacotes de relógio, telemóvel/Android TV e Windows.

## Atualizar

Atualize o relógio em **Sobre → Instalar / atualizar relógio** no telemóvel.

## Validação

Compilação Release e inspeção do APK Wear OS, incluindo launcher, ícones,
metadados Data Layer, runtimes ARM e código .NET incorporado. Assinatura dos
APKs verificada contra a identidade de produção anterior.
