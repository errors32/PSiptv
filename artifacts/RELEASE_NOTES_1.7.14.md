# PSiptv 1.7.14 — Corrigir o comando do relógio

- Corrige `java.lang.UnsupportedOperationException` nas listas de dispositivos
  e canais do Wear OS. Os adaptadores eram criados a partir de arrays fixos;
  limpar ou acrescentar entradas lançava a exceção no Android.
- Usa listas mutáveis nos dois adaptadores. A correção abrange o arranque,
  a mudança entre Bluetooth e Wi-Fi, a procura de dispositivos e os canais.
- Mantém os ícones adaptativo e monocromático da versão 1.7.13.
- Inclui APKs de telemóvel/Android TV e Wear OS assinados com a identidade de
  produção existente, além do pacote Windows x64.

## Atualizar

Atualize o telemóvel e use **Sobre → Instalar / atualizar relógio** para instalar
`PSiptv.Remote.Wear.v1.7.14.apk`. Também pode instalar o APK Wear por ADB.

## Validação

390 verificações automáticas aprovadas. Uma instrumentação Android API 36
reproduziu a exceção com o construtor antigo e validou três ciclos de limpar,
preencher e voltar a limpar cada adaptador com o construtor corrigido.
Compilação Release e verificação dos APKs assinados. Não havia um Watch 5 físico
ligado para testar uma sessão Bluetooth completa com o rádio Samsung.
