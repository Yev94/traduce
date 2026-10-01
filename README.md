# Traduce

**Selecciona una región con Alt+T y tradúcela con tu IA.** Aplicación pequeña para Windows, con OCR local y soporte de texto e imágenes.

[Descargar instalador](https://github.com/Yev94/traduce/releases/latest/download/Traduce-Setup.exe) · [Versión ZIP](https://github.com/Yev94/traduce/releases/latest/download/Traduce-Windows-x64.zip) · [Todas las versiones](https://github.com/Yev94/traduce/releases)

Traduce al **español** desde otros idiomas. Si el original está en español, traduce al **inglés**. Funciona sobre navegadores, documentos, imágenes y cualquier programa que Windows pueda capturar.

## Empezar

1. Descarga y abre **Traduce-Setup.exe**. Instala para tu usuario, sin administrador. Puedes dejar activado el inicio con Windows.
2. En la primera apertura elige tu conexión:
   - **ChatGPT · Codex** o **Claude Code**: pulsa **Instalar cliente** si lo necesitas, después **Iniciar sesión oficial**. Completa el acceso en el navegador del proveedor. Si ya tienes una sesión, pulsa **Comprobar** y **Guardar**.
   - **API**: pega tu clave, pulsa **Cargar modelos** si quieres comprobarla o elegir otro modelo y **Guardar**. Cada proveedor conserva su propia configuración.
   - **Ollama**: inicia Ollama, descarga un modelo y selecciónalo con **Cargar modelos**.
3. Pulsa **Alt+T**, arrastra sobre el contenido y suelta. Windows reconoce texto localmente; si no obtiene texto, Traduce envía el recorte como imagen.

**Requisitos:** Windows 10/11 x64 y .NET Framework 4.8. No hace falta instalar un SDK, Python ni Node para usar Traduce. Las conexiones de cuenta necesitan su cliente oficial. WinGet (App Installer) permite instalarlo desde la aplicación; también se ofrece un enlace a la instalación oficial.

La versión ZIP no requiere instalación: extrae ambos archivos `Traduce.exe` y `Traduce.exe.config` juntos y abre el EXE. No añade inicio automático. Los ajustes siguen guardándose por usuario en AppData. Al cambiar de ordenador, vuelve a iniciar sesión o introduce la clave allí; no copies credenciales entre equipos.

Los ejecutables de esta primera versión no tienen firma de editor; Windows puede mostrar un aviso al descargarlos. Las sumas SHA-256 de cada versión están en `SHA256SUMS.txt`.

## Proveedores

| Conexión | Acceso | Modelo inicial |
| --- | --- | --- |
| ChatGPT · Codex | Tu sesión en el cliente oficial de Codex | `gpt-6-luna`, razonamiento bajo |
| Claude Code | Tu sesión en el cliente oficial de Claude Code | `haiku` |
| OpenAI API | Clave de OpenAI Platform | `gpt-6-luna`, sin razonamiento adicional |
| Claude API | Clave de Claude Console | `claude-haiku-4-5` |
| Google Gemini API | Clave de AI Studio | `gemini-3.8-flash` |
| OpenRouter API | Clave de OpenRouter | Elígelo entre los modelos de tu cuenta |
| Ollama local | Servidor local en `localhost:11434` | Un modelo que tengas descargado |
| Otra API compatible con OpenAI | URL base, clave y modelo | Configurable |

Puedes escribir otro identificador de modelo. La disponibilidad depende de tu cuenta. Para traducir imágenes, el modelo debe admitir visión; con un modelo de texto puedes traducir lo reconocido por OCR.

Las suscripciones se utilizan **a través del cliente oficial instalado y autenticado por cada usuario**, con sus límites y condiciones. Traduce no implementa un acceso propio a las suscripciones ni extrae sus tokens. No todas las suscripciones de IA permiten este uso. Las APIs tienen cuotas y facturación independientes de las suscripciones web.

La opción genérica utiliza `POST /chat/completions` y `GET /models`. Admite proveedores compatibles, no cualquier API propietaria sin adaptación. La URL debe ser HTTPS; HTTP se permite únicamente en el equipo local. Cambiar esa URL descarta la clave anterior para evitar enviarla al servidor nuevo. Si el servidor no ofrece `/models`, escribe el modelo manualmente.

## Uso diario

- **Esc** o clic derecho cancela la selección. Otro Alt+T sustituye la petición pendiente; una respuesta antigua no puede sobrescribir el resultado nuevo.
- **Copiar** copia la traducción. **Pegar** acepta texto o imagen del portapapeles. **Imagen** abre una imagen directamente, sin OCR previo. **Texto** permite revisar o editar el original.
- La ventana tiene el ancho del recorte, limitado al área útil del monitor. Se coloca encima o debajo según el espacio, permanece por delante y conserva el foco en la aplicación de origen. Admite varias pantallas y distintas escalas.
- Cerrar la ventana la deja en la bandeja, con el atajo activo. Para salir, usa **Salir** en el icono junto al reloj.
- **Ajustes** permite cambiar proveedor, modelo, clave y atajo. La caché guarda hasta 12 resultados en memoria; se descarta al salir.

No cambia el portapapeles al capturar ni reemplaza el contenido de la aplicación original. El OCR depende de los idiomas instalados en Windows: puede omitir texto o perder formato. Para evitar un reconocimiento parcial, pega una captura o usa Imagen. El contenido que Windows capture en negro no será legible.

## Privacidad y registros

La captura completa se mantiene en memoria mientras seleccionas. Solo se procesa el recorte. Se envía **el texto reconocido o la imagen recortada** al proveedor seleccionado cuando pides traducir. Con Ollama local, la petición va al servidor local configurado.

Las claves API se cifran mediante **Windows DPAPI para el usuario actual**. Traduce no muestra ni copia los tokens de los clientes oficiales. No guarda un historial de textos o traducciones. Las imágenes temporales usadas por Codex se eliminan al terminar o cancelar; un cierre forzado puede dejar archivos `Traduce-*` en TEMP. La retención y los registros de los proveedores/clientes se rigen por sus políticas.

Los registros locales están en `%LOCALAPPDATA%\Traduce\logs\captures-AAAA-MM-DD.jsonl`. Registran aplicación activa y bajo el recorte (ejecutable, PID, clase de ventana), modo `text`/`image`, motivo del OCR, número de caracteres, proveedor/modelo, tiempos, geometría/monitor y resultado. **No contienen títulos de ventanas, texto, imágenes, claves ni traducciones.** No se suben a ningún servicio.

Para analizar la detección, cuenta solo eventos `capture` y agrupa por `active_app.process`, `mode` y `ocr_reason`; los eventos `finished` sirven para resultado y duración. La proporción de texto detectado no mide su exactitud. Pegar, Imagen y la traducción manual quedan fuera del registro de recortes.

## Actualizar y desinstalar

Ejecuta el nuevo instalador para actualizar. Conserva ajustes y registros y cierra la instancia anterior. Para desinstalar, usa **Configuración de Windows → Aplicaciones → Traduce**. La desinstalación conserva los datos de usuario; puedes borrarlos eliminando `%LOCALAPPDATA%\Traduce` después de salir. No desinstala los clientes oficiales de IA.

El inicio automático usa el acceso directo `Traduce.lnk` de `shell:startup` con `--background`. Puedes desactivarlo en Aplicaciones de inicio de Windows o quitando ese acceso directo. `Traduce.exe --quit` permite cerrar la aplicación al actualizarla.

## Compilar

Código C# 5 / WinForms, .NET Framework 4.8 y Windows 10/11 SDK (`Windows.winmd`). Sin dependencias NuGet. En PowerShell, desde la carpeta del repositorio:

```powershell
.\build.ps1 -ContractTest   # Compila y prueba contratos API/CLI sin credenciales
.\build.ps1 -Test           # Añade interfaz, OCR, portapapeles y arranque real
.\package.ps1 -SkipBuild    # Instalador + ZIP + hashes; requiere Inno Setup 6
```

Inno Setup se puede instalar con `winget install --id JRSoftware.InnoSetup --exact --scope user`. Para compilar, empaquetar e instalar localmente: `./install.ps1 -Launch`.

Las pruebas completas requieren Windows con escritorio. Para comprobar el atajo y la colocación sobre los monitores conectados, cierra Traduce y ejecuta `./build.ps1 -DesktopTest` con el escritorio desbloqueado; no cambies de ventana durante la prueba. `./build.ps1 -LiveTest` envía **tres peticiones reales a Codex** usando tu sesión: texto, texto de OCR e imagen. Las pruebas ordinarias no consumen servicios.

GitHub Actions compila, ejecuta las pruebas de contratos y genera los paquetes. No ejecuta pruebas de escritorio/OCR ni servicios de pago en el runner. Los binarios para usuarios se publican en Releases; los artefactos de CI permiten revisar cada compilación.

## Documentación oficial

[Codex: autenticación](https://developers.openai.com/codex/auth) · [Codex no interactivo](https://developers.openai.com/codex/noninteractive) · [GPT-6 Luna API](https://developers.openai.com/api/docs/models/gpt-6-luna) · [Claude Code: CLI](https://code.claude.com/docs/en/cli-reference) · [Claude Code: condiciones de integración](https://code.claude.com/docs/en/legal-and-compliance) · [Claude Messages](https://platform.claude.com/docs/en/api/messages/create) · [Gemini compatible con OpenAI](https://ai.google.dev/gemini-api/docs/openai) · [OpenRouter](https://openrouter.ai/docs/quickstart) · [Ollama](https://docs.ollama.com/api/openai-compatibility)

Proyecto independiente, sin afiliación con estos proveedores. Licencia MIT.

## English

Small Windows screen translator. Press **Alt+T**, select a region, and translate using your own AI account or API. Local Windows OCR sends text when available, otherwise only the selected image. Spanish is translated to English; other languages to Spanish. Download the installer or ZIP above. Supports official Codex/Claude Code clients, OpenAI, Anthropic, Gemini, OpenRouter, Ollama and OpenAI-compatible APIs. UI and primary documentation are currently in Spanish. Windows 10/11 x64; MIT licensed.
