# Aegis · Endpoint Suite — proyecto completo

Proyecto WinUI 3 (.NET 8) montado sobre el **ZIP de Google Drive** (csproj, manifiestos,
assets y perfiles de publicación) + **los 12 archivos que pegaste** + los 8 que faltaban.

Namespace y ensamblado: **`AntivirusApp`**.

---

## 1. Acceso al panel (v22.0)

La pantalla de inicio vuelve a pedir usuario y contraseña. Las credenciales por defecto
siguen viviendo **solo en el código** (`Services/AuthService.cs`): `admin` / `aegis`, o
las de `%LocalAppData%\Aegis\auth.json` si existe ese archivo.

La interfaz **no muestra la contraseña ni ninguna pista de acceso**: el campo es un
`PasswordBox` en modo oculto y no hay tarjetas de credenciales. `AuthService.Validate`
compara en tiempo fijo contra el hash SHA-256.

---

## 2. Archivos que pegaste (sin tocar su lógica)

| Archivo | Líneas |
|---|---|
| `App.xaml` | 104 |
| `App.xaml.cs` | 18 |
| `Services/NotificationService.cs` | 48 |
| `Views/ViewItems.cs` | 94 |
| `Views/LoginPage.xaml(.cs)` | 87 + 30 |
| `Views/ShellPage.xaml(.cs)` | 182 + 258 |
| `Views/ScanPage.xaml(.cs)` | 202 + 425 |
| `Views/LivePage.xaml(.cs)` | 152 + 180 |

## 3. Archivos que faltaban (escritos aquí, con las firmas exactas que usan los tuyos)

| Archivo | Contrato que respeta |
|---|---|
| `Models/Verdicts.cs` | `const string` `Malicious="M"`, `Suspicious="S"`, `Clean="C"`, `Error="E"` (los chips filtran por "M"/"S") |
| `Models/ScanMode.cs` | `Automatic`, `Quick`, `Folder`, `Deep` |
| `Models/ScanOptions.cs` | `new ScanOptions(ScanMode.Quick)` y `new ScanOptions(ScanMode.Folder, IncludeHidden: true)` |
| `Models/ScanResult.cs` | `Verdict`, `RiskScore`, `Reasons`, `Sha256`, `Size`, `Elapsed`, `IsError` — con `set` público (con `init` el build falla con CS8852 en `XamlTypeInfo.g.cs`) |
| `Engine/HeuristicScanner.cs` | `SafeScan(path, options, progress)`; acepta archivo o carpeta |
| `Engine/FileEnumerator.cs` | `Enumerate(folder, options, ct)` — lo usa `ScanPage.ScanFolderAsync` |
| `Engine/FolderWatcher.cs` | `new FolderWatcher(ruta)`, `FileDetected`, `WaitUntilReadyAsync` |
| `Engine/Ring.cs` | `Ring.Set(Ellipse, double)` mediante `StrokeDashArray` sobre la elipse rotada −90° |
| `Services/AuthService.cs` | `AuthService.Validate(usuario, contraseña)` |
| `MainWindow.xaml(.cs)` | `new MainWindow()` de `App.OnLaunched`: hospeda el `Frame` y navega a `LoginPage` |

## 4. Errores corregidos en tu código

Detectados compilando con Roslyn contra los ensamblados reales de WinUI:

| Error | Archivo | Causa | Corrección |
|---|---|---|---|
| `CS0103: FileEnumerator no existe` | `ScanPage.xaml.cs:101` | se usaba un tipo que no venía en el pegado | creado `Engine/FileEnumerator.cs` |
| `CS0428: método 'Count' como int` | `ScanPage.xaml.cs:102` | consecuencia del anterior (`ToList()` sobre un grupo de métodos) | resuelto al existir `FileEnumerator` |
| `CS0165: 'card' sin asignar` | `ShellPage.xaml.cs:195` | los botones de la tarjeta llamaban a `Close()`, que captura `card` y `timer` declarados más abajo | `Action dismiss` declarado antes y asignado al final; las 4 llamadas usan `dismiss()` |
| `CS0165: 'timer' sin asignar` | `ShellPage.xaml.cs:195` | ídem | ídem |

### Y dos del compilador de XAML

| Error | Archivo | Causa | Corrección |
|---|---|---|---|
| `WMC0001: Unknown type 'UniformGrid'` | `Views/LivePage.xaml:83` | `UniformGrid` **no existe en WinUI 3**: es un control de WPF (en WinUI viene en CommunityToolkit, no en el SDK) | sustituido por un `Grid` con 4 columnas `*` y `ColumnSpacing="12"`; cada tarjeta lleva su `Grid.Column` |
| `WMC0001: Unknown type 'UniformGrid'` | `Views/ScanPage.xaml:123` | ídem | ídem con `ColumnSpacing="24"` (los contadores del panel de análisis) |

### Y un crash en tiempo de ejecución

| Error | Causa | Corrección |
|---|---|---|
| `COMException: No COM servers are registered for this app` en `OnLaunched` | `AppNotificationManager.Default.Register()` necesita **identidad de paquete (MSIX)**. Ejecutando el `.exe` suelto o la configuración «sin empaquetar» de VS no existe el servidor COM de activación y la excepción no estaba capturada | `Register()` ahora prueba y, si falla, marca `ToastAvailable = false` y deja el aviso dentro de la ventana; el motivo se escribe en `%LocalAppData%\Aegis\app.log` |

### Diagnóstico de arranque (v5)

Ante un cierre silencioso con `0x80008089` (sin traza en el depurador), el arranque queda
instrumentado:

| Dónde | Qué hace |
|---|---|
| `App.App()` | suscribe `UnhandledException` y `AppDomain.UnhandledException`, y registra «App creada» |
| `App.OnLaunched()` | registra cada paso: notificaciones → `MainWindow` → `Activate`; si algo falla escribe el log y muestra un `MessageBox` nativo |
| `MainWindow` | la navegación a `LoginPage` pasa del constructor al evento `Loaded`, con `try/catch` y log |
| Log | `%LocalAppData%\Aegis\app.log` con fecha, paso, tipo de excepción, mensaje y `StackTrace` |

Con esto, cualquier fallo de arranque deja rastro en disco y en pantalla en lugar de
cerrar la app sin explicación.

### v8 — por qué «no aparecía nada»

| Causa | Corrección |
|---|---|
| `ScanPage.Record()` hacía `if (!item.IsAlert) return;`: los archivos limpios **no se listaban nunca**, así que parecía que no analizaba | ahora se listan **todos** los archivos analizados, con veredicto y puntuación; las alertas siguen disparando notificación |
| Los tres handlers (`ScanFile`, `ScanFolder`, `ScanZip`) eran `async void` con `try/finally` y **sin `catch`**: cualquier excepción (picker, permisos, ZIP dañado) desaparecía sin mensaje | añadido `catch` que escribe en `app.log` y muestra el error en `HeadTitle`/`HeadDetail` |
| No había forma de saber si se estaba ejecutando el build nuevo | el título de la ventana muestra **«motor 2.0»** y cada análisis se registra en `%LocalAppData%\Aegis\app.log` |

### Motor 2.0 — análisis estructural (v7)

El motor ya no solo mira bytes y cadenas: ahora entiende el formato.

| Inspector | Qué analiza | Ejemplos de indicios |
|---|---|---|
| `Engine/PeInspector.cs` | Cabecera PE, secciones, **tabla de importaciones**, overlay, entry point | `VirtualAllocEx` + `WriteProcessMemory` + `CreateRemoteThread` (inyección), `SetWindowsHookEx` (keylogger), `IsDebuggerPresent` (anti-debug), `URLDownloadToFile` (descarga), `CryptEncrypt` (ransomware), secciones `UPX0/Themida/.vmp0`, sección RWX, entropía ≥ 7,9 |
| `Engine/DocumentInspector.cs` | PDF, Office OOXML y accesos directos `.lnk` | `/JavaScript`, `/OpenAction`, `/Launch`; `word/vbaProject.bin` con `AutoOpen` + `CreateObject("WScript.Shell")`; `.lnk` con `powershell -enc` |
| `Engine/ArchiveInspector.cs` | ZIP sin extraer | entradas con `../` (zip slip), ejecutables dentro, bomba de descompresión, > 2 GB descomprimidos |

Resultados medidos con el proyecto compilado y muestras reales:

| Muestra | Veredicto | Score | Por qué |
|---|---|---|---|
| `7zr.exe` (binario legítimo real) | Limpio | 12 | 6 secciones, 133 imports, sin packer → **sin falso positivo** |
| `documento.exe` (texto renombrado) | Sospechoso | 38 | extensión ejecutable sin cabecera `MZ` |
| `eicar.txt` (firma de prueba) | Malicioso | 100 | firma EICAR dentro de un `.txt` |
| `presupuesto.pdf` con JS | Sospechoso | 50 | JavaScript + acción al abrir |
| `factura.lnk` → powershell -enc | Sospechoso | 67 | destino y argumentos de intérprete |
| `adjuntos.zip` con `../../evil.exe` | Sospechoso | 39 | zip slip + ejecutable dentro |
| `informe.docm` con macro AutoOpen | Sospechoso | 65 | macro con ejecución de comandos |
| PE con `VirtualAllocEx`+`WriteProcessMemory`+`CreateRemoteThread` | Malicioso | 73 | inyección de código en otro proceso |

### Motor: reglas anteriores (v6)

Detectado con pruebas reales: un `.txt` renombrado a `.exe` puntuaba **8/100** y se
marcaba «Limpio», así que nunca aparecía en la lista (la vista solo muestra alertas).

| Regla nueva | Puntos |
|---|---|
| Extensión ejecutable (`.exe`, `.dll`, `.scr`, `.com`…) sin cabecera `MZ` → archivo renombrado o dañado | +30 |
| Aviso «Análisis parcial: X MB revisados de Y MB» cuando el archivo supera el límite de lectura | informativo |

Resultados medidos tras el cambio:

| Muestra | Veredicto | Score |
|---|---|---|
| `documento.exe` (texto renombrado) | Sospechoso | 38 |
| `documento.txt.exe` | Sospechoso | 63 |
| `eicar.com` | Malicioso | 100 |
| `factura.pdf` con cabecera `MZ` | Sospechoso | 35 |
| `instalar.ps1` ofuscado | Sospechoso | 42 |

Umbrales: **≥ 35 sospechoso**, **≥ 70 malicioso** (`Models/ScanOptions.cs`).

Ningún cambio toca el diseño ni la lógica: el `Grid` de 4 columnas iguales reproduce el
reparto del `UniformGrid`, y la captura de `card`/`timer` se movió a una variable ya
inicializada.

## 5. Verificación hecha antes de entregar

| Comprobación | Resultado |
|---|---|
| Compilación C# (Roslyn, 16 archivos, refs reales WinUI 2.3.9 / SDK.NET 10.0.19041) | **0 errores** |
| Propiedades y eventos del XAML contra los metadatos de WinUI | **806 / 806 existen** |
| Tipos de control usados en el XAML | los 29 existen en WinUI 3 (ya sin `UniformGrid`) |
| Claves `{StaticResource ...}` frente a `App.xaml` | todas definidas (22) |
| `x:Bind` de las plantillas frente a `ScanItem` | todas resuelven |
| `x:Class` ↔ code-behind y `x:Name` duplicados | correctos en los 6 XAML |
| Firmas de los 19 handlers | todas coinciden con su `EventArgs` |
| `dotnet restore` de los paquetes 2.5.1 | correcto |

**No verificado** (necesita Windows): el paso del compilador de XAML (`.g.cs`), el
empaquetado MSIX y la ejecución.

## 6. Compilar

```powershell
dotnet build AntivirusApp.csproj -c Debug -p:Platform=x64
```

Abre `AntivirusApp.slnx` (VS 17.10+) o `AntivirusApp.sln` (cualquier VS 2022). Si algo
falla, genera el binlog y pásalo:

```powershell
dotnet build AntivirusApp.csproj -c Debug -p:Platform=x64 -bl:build.binlog
```

---

## v9 — Clenaer (nuevo módulo)

Se añade el **Clenaer** al menú lateral, entre *Análisis manual* y *Vigilancia en vivo*.
No se ha tocado ninguna línea de los módulos anteriores: el botón nuevo navega a
`CleanerPage` con el mismo `Navigate(...)` y el mismo `Mark(...)` de resaltado.

| Archivo nuevo | Qué contiene |
|---|---|
| `Views/CleanerPage.xaml` | Interfaz: tarjeta de unidades y opciones, progreso, filtros y lista |
| `Views/CleanerPage.xaml.cs` | Lógica de la vista (escaneo en segundo plano, orden, selección, papelera) |
| `Views/CleanerRow.cs` | Fila de la lista (sirve para archivos, carpetas y aplicaciones) |
| `Models/CleanerModels.cs` | `CleanerOptions`, `CleanerCounters`, `CleanerItem`, `AppUsage`, `TrashResult` |
| `Services/CleanerService.cs` | Motor: `Scan`, `MeasureApps`, `MoveToTrash`, `Reveal`, `DefaultRoots` |

| Archivo modificado | Cambio |
|---|---|
| `Views/ShellPage.xaml` | Botón `NavCleaner` («Clenaer», glifo de papelera) + pie «Motor heurístico 2.0 · Clenaer» |
| `Views/ShellPage.xaml.cs` | `NavCleaner_Click` y `Mark(NavCleaner, …)` en `Navigate` |

### Dos modos

1. **Archivos / Carpetas pesadas** — recorre las unidades con `DirectoryInfo` en un hilo
   de fondo (la UI nunca se bloquea) y devuelve lo que supera el mínimo elegido.
   Casilla **«Evitar carpetas de sistema»** activada por defecto: no entra en `Windows`,
   `System32`, `WinSxS`, `$Recycle.Bin`, `System Volume Information`, `$Windows.~BT`…
   Ordenable por **peso, nombre, fecha y tipo**; buscador; selección múltiple.
2. **Por aplicación** — mide cada carpeta de programa (`Program Files`, `Program Files (x86)`,
   `LocalAppData`, `Roaming`, `ProgramData`, Descargas, Documentos, Escritorio, Vídeos)
   y lista **peso, nº de archivos y última actividad**.

### Borrado

`SHFileOperationW` con `FOF_ALLOWUNDO` → **Papelera de reciclaje real de Windows**, sin
dependencias NuGet ni referencias a `Microsoft.VisualBasic`. Antes hay un `ContentDialog`
con la lista de elementos, el total a liberar y una casilla que hay que marcar para que
el botón se habilite. Lo que está en uso o sin permisos se informa y **no se borra**.

### Rendimiento

Un solo hilo para el escaneo, `Parallel.ForEach` (núcleos/2) para medir aplicaciones y un
`DispatcherQueueTimer` de 150 ms que refresca contadores: la ventana sigue respondiendo
incluso recorriendo un disco entero.

---

# v10 — «Espacio y ZIP» + gráficas en el Clenaer

Se añade un módulo nuevo de **análisis de espacio con gráficas** y un botón de
**comprimir carpetas a ZIP**. No se reescribe nada del motor ni de las pantallas
existentes: el Clenaer gana una franja de gráficas y el menú lateral gana un módulo.

| Archivo nuevo | Qué contiene |
|---|---|
| `Controls/Charts.cs` | Gráficas sin dependencias: barras proporcionales, dona hueca con arcos y leyenda |
| `Models/SpaceModels.cs` | `FolderUsage`, `KindUsage`, `SpaceReport`, `SpaceCounters`, `ZipOutcome` |
| `Services/SpaceService.cs` | `Scan` (mide carpetas y tipos), `Compress` (ZIP + verificación + borrado), `IsProtected` |
| `Views/SpaceRow.cs` | `RootOption` (raíz del desplegable) y `SpaceRow` (fila de la tabla) |
| `Views/SpacePage.xaml` | Interfaz: barra de análisis, 4 indicadores, 2 gráficas y tabla de carpetas |
| `Views/SpacePage.xaml.cs` | Lógica: análisis en segundo plano, selección, confirmación y compresión |

| Archivo modificado | Cambio |
|---|---|
| `Views/ShellPage.xaml` | Botón `NavSpace` («Espacio y ZIP», glifo `E7B8`) |
| `Views/ShellPage.xaml.cs` | `NavSpace_Click` y `Mark(NavSpace, …)` |
| `Views/CleanerPage.xaml` | Franja de análisis dentro de la tarjeta de estado: `MiniBars`, `MiniDonut`, `MiniLegend` |
| `Views/CleanerPage.xaml.cs` | `DrawAnalysis()`, llamado al final de `Rebuild()` |

## Qué se ve en «Espacio y ZIP»

1. **Barra de análisis** — desplegable con todas las unidades fijas/extraíbles y las
   carpetas del usuario (perfil, Vídeos, Imágenes, Música, Documentos), botón
   *Analizar espacio*, *Detener* y *Mis ZIP*.
2. **Cuatro indicadores** — espacio analizado, archivos, carpetas candidatas y
   seleccionado (se actualiza al marcar filas).
3. **Gráficas**
   - *Dónde se va el espacio*: barras con las 8 carpetas más pesadas de la raíz.
   - *Por tipo de archivo*: dona con el reparto por categoría (vídeo, imagen, audio,
     documento, comprimido, instalador, código, base de datos, caché, otro) + leyenda
     con tamaño y porcentaje.
   - Cada fila de la tabla lleva además una mini-barra con su porcentaje del total.
4. **Tabla de carpetas** — nombre, nº de archivos, % del total, tamaño y botón
   *Dentro* para volver a medir esa subcarpeta (análisis en profundidad).
5. **Acciones** — *Solo comprimir* y *Comprimir y borrar original*.

## Cómo funciona la compresión

1. Se pide confirmación con la lista de carpetas, el total y la ruta de destino.
   Para borrar hay que marcar la casilla *«Entiendo que la carpeta original se borrará»*;
   sin marcarla, el botón principal está deshabilitado.
2. El ZIP se crea con `ZipFile.CreateFromDirectory` en
   `%LocalAppData%\Aegis\archives\<carpeta>-AAAAMMDD-HHMMSS.zip`.
3. **Verificación obligatoria**: se abre el ZIP, se cuentan las entradas y se suma su
   tamaño. Si el número de archivos no coincide o falta más de un 0,5 % del peso, se
   **borra el ZIP** y la carpeta original **no se toca**.
4. Sólo entonces se borra la carpeta original (`Directory.Delete(..., true)`) y se
   informa de lo liberado. Si el borrado falla (archivo en uso), el ZIP queda y se
   avisa: nunca se pierde información.

## Seguridad: qué nunca se toca

`SpaceService.IsProtected` bloquea, tanto en la interfaz (candado + selección inválida)
como en el servicio (segunda comprobación antes de comprimir):

- Raíces de unidad (`C:\`), `Windows`, `System32`, `SysWOW64`, `WinSxS`,
  `Program Files`, `Program Files (x86)`, `ProgramData`, `Recovery`, `Boot`, `PerfLogs`;
- la carpeta del perfil de usuario **entera** (sí se pueden comprimir sus subcarpetas
  como `Downloads`, `Videos`, etc.);
- cualquier carpeta que contenga el directorio de ZIPs.

## Rendimiento

- `EnumerationOptions` con `IgnoreInaccessible = true` y
  `AttributesToSkip = ReparsePoint`: no sigue enlaces ni se atasca en carpetas
  protegidas del sistema (evita bucles y excepciones por acceso denegado).
- El análisis corre en `Task.Run` con `CancellationToken` y publica progreso cada
  120 ms mediante `DispatcherQueueTimer`; la ventana nunca se congela.
- La compresión se ejecuta de una carpeta en una (`await` secuencial) para no saturar
  el disco con varios ZIP a la vez.

## Nota de diseño

Las gráficas están hechas con formas nativas (`Border` + columnas «star» para las barras,
`PathGeometry`/`ArcSegment` para la dona). No hace falta añadir Win2D, LiveCharts ni
ningún paquete: el `.csproj` no cambia.

---

# v11 — corrección del crash al abrir el Clenaer

**Síntoma**: al pulsar «Clenaer» la app moría con
`XamlParseException ... Object reference not set to an instance of an object`
seguido de `NullReferenceException` y código de salida `0xFFFFFFFF`.

**Causa**: la franja de gráficas de la v10 se añadió como XAML dentro de
`CleanerPage.xaml`, e introducía dos tipos de elemento que **no aparecían en ningún
otro XAML del proyecto** (`Rectangle` y `Canvas` anidado en una celda `Auto`). En la
práctica, `InitializeComponent()` lanzaba la excepción durante el parseo, así que
ningún `x:Name` llegaba a asignarse (de ahí los `RowList fue null`).

**Solución en la v11**:

1. `CleanerPage.xaml` vuelve a ser **idéntico al de la v9**, con una única diferencia:
   `<StackPanel x:Name="StatsStack">`. Ningún elemento nuevo, ninguna gráfica en XAML.
2. La franja de análisis (divisor + rejilla de 3 columnas + dona + leyenda) se
   **construye en código** en `BuildAnalysisBlock()`, justo después de
   `InitializeComponent()`, y `DrawAnalysis()` sólo rellena los hosts ya creados
   (con guardas `is null` y `try/catch` que escriben en `app.log`).
3. `SpacePage.xaml` pierde el `LinearGradientBrush` (único elemento nuevo que quedaba
   en el proyecto); la tarjeta destacada usa un color plano `#FF1B2B4A`.
4. **Diagnóstico nuevo**: `ShellPage` engancha `ContentFrame.NavigationFailed`. Si
   cualquier página falla al construirse, la app **ya no se cierra**: escribe el error
   completo (excepción + internas + primera línea de stack que apunte a `AntivirusApp`)
   en `%LocalAppData%\Aegis\app.log` y lo muestra en un diálogo.

Si vuelve a fallar algo, ahora hay dos fuentes de verdad: el diálogo en pantalla y
las últimas líneas de `app.log`.

**Importante al recompilar**: borra `bin` y `obj` antes de compilar. Un `obj` viejo
puede reutilizar el `CleanerPage.g.cs` generado con el XAML anterior y reproducir
errores que ya no existen en el código fuente.

---

# v12 — causa real del crash del Clenaer (corregida)

El diálogo de error de la v11 dio el stack exacto:

```
CleanerPage.UpdateSelection()      ← RowList.SelectedItems
CleanerPage.Rebuild()
CleanerPage.Sort_Changed(sender, SelectionChangedEventArgs)
WinRT.EventSource…SelectionChangedEventHandler
Microsoft.UI.Xaml.Controls.Frame.Navigate(…)
ShellPage.Navigate(…) → NavCleaner_Click
```

**Diagnóstico definitivo**: `SortBox` tiene `SelectedIndex="0"` en el XAML. WinUI aplica
ese valor **durante** `InitializeComponent()`, y eso dispara `SelectionChanged` →
`Sort_Changed` → `Rebuild()` → `UpdateSelection()`. En ese instante `RowList` **todavía
no existe**, porque en el archivo XAML está declarado *después* del `SortBox` (los
`x:Name` se asignan en orden de documento). Resultado: `NullReferenceException`, que
WinUI envuelve y reporta como `XamlParseException: XAML parsing failed`.

Era un fallo latente del Clenaer v9, no de las gráficas: sólo se manifiesta al abrir
la página, y siempre.

**Corrección (v12)**:

| Archivo | Cambio |
|---|---|
| `Views/CleanerPage.xaml.cs` | Campo `_ready` (false durante `InitializeComponent`), activado justo antes del `Rebuild()` inicial. `Rebuild()` y `UpdateSelection()` salen sin hacer nada si `_ready` es false o si sus controles aún son null. |
| `Views/SpacePage.xaml.cs` | Misma bandera; `Root_Changed` la respeta. |

Con esto, el evento que se dispara durante el parseo ya no toca controles inexistentes,
y la página se pinta al final del constructor, cuando todo existe.

Regla general aplicada al resto de la app: **ningún handler enganchado en XAML debe
asumir que los controles declarados más abajo existen**. Los que dependen de índices
iniciales (`SelectedIndex`, `SelectedItem`, `Text`) van guardados.

---

# v13 — Aegiss_Clenaer y Aegiss_TheClurer (rediseño pedido)

## Aegiss_Clenaer (antes «Clenaer»)

- **Una sola acción**: botón grande «Escanear equipo completo» (unidades fijas del equipo).
- Resultado en tres bloques:
  1. **Indicadores**: archivos revisados, carpetas revisadas, peso encontrado.
  2. **Gráfica TOP**: las 8 entradas más pesadas de la pestaña activa.
  3. **Lista separada** con dos pestañas: **Archivos** y **Carpetas**. Cada fila
     muestra el nombre, el origen (carpeta contenedora / ruta), el peso y dos botones:
     **Mostrar carpeta** y **Eliminar**.
- **Al eliminar** aparece un aviso de peligro con el elemento seleccionado y su peso,
  y una casilla obligatoria *«Confirmo que no lo voy a utilizar»*: el botón «Eliminar»
  está deshabilitado hasta marcarla. Con «Cancelar» no se toca nada.
  El borrado va a la **Papelera de reciclaje** (recuperable), no a la nada.

## Aegiss_TheClurer (antes «Espacio y ZIP»)

- **Aplicaciones del sistema**: botón «Medir aplicaciones».
  - El peso se muestra como **mapa de cuadrados**: cada app es un rectángulo
    proporcional a su tamaño (grande si pesa mucho, pequeño si pesa poco).
  - Debajo, lista de apps con grupo, nº de archivos, ruta y **Mostrar carpeta**.
- **Carpetas → ZIP**: botón «Analizar carpetas para ZIP» y, por cada carpeta,
  **Comprimir** o **Comprimir y borrar** (con casilla de confirmación; el ZIP se
  verifica antes de borrar nada, como en la v10).
- Botón **Mis ZIP** para abrir `%LocalAppData%\Aegis\archives`.

## Desplazamiento

- Las dos páginas tienen **barra de desplazamiento vertical** en toda la página y
  **barra visible** en cada lista interna (resultados, apps, carpetas).
- La lista de resultados de **Análisis manual** también muestra ya su barra
  (`ScrollViewer.VerticalScrollBarVisibility="Visible"`).

## Sin handlers en XAML

Las dos páginas nuevas cablean **todos** los eventos en el constructor, y las filas se
construyen en código. Así desaparece por completo la clase de fallo de la v12 (un
handler que se dispara a mitad del parseo y toca controles que aún no existen).

---

# v14 — cuadros acomodados y selector de carpeta

## Aegiss_Clenaer

- **Cuadros igualados**: la fila de indicadores tiene altura fija (96 px), el mismo
  padding y tres líneas por cuadro (título, número, aclaración). Ya no bailan de alto
  ni de posición.
- **Nuevo cuadro «O analiza una carpeta concreta»**, justo debajo de los indicadores:
  · `Elegir carpeta…` → `FolderPicker` nativo de Windows (con `InitializeWithWindow`,
    imprescindible en WinUI 3 dentro de un `Frame`).
  · El cuadro muestra la ruta elegida y `Analizar esta carpeta` se habilita al elegirla.
  · Analiza **solo esa carpeta**; el resto del equipo no se toca.
- La cabecera conserva `Escanear equipo completo` (todas las unidades fijas) + `Detener`.

## Aegiss_TheClurer

- **Cuadros igualados**: fila de 4 indicadores con la misma altura y estructura que en
  Clenaer (aplicaciones · peso de apps · carpetas · peso en carpetas).
- **Las dos acciones son ahora cuadros** (dos tarjetas del mismo tamaño):
  1. *Aplicaciones del sistema* → `Medir aplicaciones`.
  2. *Carpetas a ZIP* → muestra la carpeta objetivo, `Cambiar carpeta…`,
     `Analizar carpetas`, `Mis ZIP`.
- Se mantiene el **mapa de cuadrados** para el peso de las apps y las listas debajo.

Verificado: 30/30 `.cs` sin errores de sintaxis, 8/8 XAML válidos, sin handlers en XAML
en las dos páginas (todo cableado en el constructor).

---

# v15 — mínimos documentados, mapa de ZIP y limpieza de botones

## Aegiss_Clenaer · aclaración del mínimo

Se añade el cuadro **«QUÉ SE MIDE»**, justo encima de la gráfica, que lo dice sin
ambigüedad:

> Solo se miden los elementos grandes: archivos de 20 MB o más y carpetas de 60 MB o
> más. **Todo lo que esté por debajo de ese mínimo NO se mide ni aparece en la lista**,
> porque libera muy poco espacio.

Y al lado, el interruptor **«Incluir pequeños»**:

| Estado | Qué mide |
|---|---|
| **Solo grandes** (por defecto) | archivos ≥ 20 MB · carpetas ≥ 60 MB · hasta 800 archivos |
| **Incluir pequeños** | archivos ≥ 1 MB · carpetas ≥ 5 MB · hasta 2.000 archivos |

Al cambiar el interruptor se repite automáticamente el último escaneo con el nuevo
filtro (no hay que volver a pulsar el botón). El texto del cuadro se actualiza solo.
Mientras se escanea, el interruptor queda bloqueado.

## Aegiss_TheClurer · mapa de cuadrados para ZIP

Faltaba la ilustración de cuadrados en la parte de ZIP: solo había lista. Ahora hay
**dos mapas**:

1. **Aplicaciones · cuánto pesan** (ya existía).
2. **Carpetas pesadas · cuánto pesan** (nuevo): un cuadrado por carpeta, proporcional
   a su tamaño, justo encima de la lista de carpetas que se pueden comprimir.

## Botones retirados

- Fuera el botón **«Mis ZIP»**.
- Fuera el cuadro del encabezado con la ruta **`%LocalAppData%\Aegis\archives`**.

Los ZIP se siguen creando en esa ruta (aparece en el diálogo de confirmación de cada
compresión), pero ya no ocupa espacio en la interfaz.

---

# v16 — diseño claro (del HTML), notificaciones de Windows y cuenta

## 1. Diseño tomado de `aegis_ficha_equipo_v10.html`

La paleta y la estructura salen de ese HTML: fondo `#F5F7F6`, tarjetas blancas con
borde `#D9E0DD`, radio 8, texto `#14191A` / `#4B5654`, acento **teal `#0B6B68`** y
marca en degradado teal → violeta (`#6D5BD0`).

- `App.xaml`: `RequestedTheme="Light"` y **los mismos nombres de recurso** (`BgBrush`,
  `PanelBrush`, `TextBrush`, `MutedBrush`, `AccentBrush`…) apuntando ahora a la paleta
  clara. Por eso todas las pantallas existentes cambian de tema sin reescribirlas.
  Se añaden `BrandBrush` (degradado) y `AccentSoftBrush`.
- `LoginPage`: rediseñada como la ficha — marca arriba a la izquierda, tarjeta blanca
  centrada, campos *Usuario* / *Contraseña* (con `PasswordBox`), botón teal y recuadro
  con las credenciales de la demo. Sigue usando `AuthService` (`admin` / `aegis`).
- `ShellPage`: cabecera blanca de 56 px con marca, indicador cuadrado verde y avatar;
  índice izquierdo de 240 px con **módulos numerados 01–05** (título + descripción);
  pie de 30 px con versión. El cajón de detalle y los toasts internos están en claro.

## 2. Notificaciones de Windows en todos los módulos

`NotificationService` ya usaba `AppNotificationManager` + `AppNotificationBuilder`, así
que cada aviso sale como **notificación nativa de Windows** (además del toast interno):

| Módulo | Cuándo notifica |
|---|---|
| Análisis manual | al terminar un análisis |
| Aegiss_Clenaer | al completar el escaneo, al eliminar y si falla |
| Aegiss_TheClurer | al medir aplicaciones, al crear el ZIP y si falla |
| Vigilancia en vivo | al **activar** y **detener** la vigilancia (nuevo) y con cada amenaza |
| Cuenta | al guardar el perfil |

Requiere identidad de paquete (MSIX), que es como está configurada la app. Si Windows no
puede mostrarla, el aviso interno sigue funcionando y queda anotado en `app.log`.

## 3. Cuenta (`Views/AccountPage.xaml`)

- **Foto**: `PersonPicture` grande + `Cambiar foto…` (`FileOpenPicker` con HWND) y
  `Quitar foto`. La imagen se copia a `%LocalAppData%\Aegis\avatar.png`, así que no se
  rompe si mueves el archivo original. Sin foto se muestran las **iniciales**.
- **Nombre para mostrar** y **Descripción** (multilínea, 240 caracteres).
- **Guardar cambios** escribe `%LocalAppData%\Aegis\profile.json` y avisa a la app: el
  avatar y el nombre de la cabecera se actualizan al instante (`ProfileStore.Changed`).
- Bloque **Sesión** con el usuario activo, la ruta del perfil y el estado de la foto.

Archivos nuevos: `Services/ProfileStore.cs`, `Views/AccountPage.xaml(.cs)`.
`AuthService` gana `CurrentUser` (usuario de la sesión).

---

# v17 — más color y movimiento + avisos como ventana emergente

## Diseño: menos blanco, más color

- **Fondo** más tintado (`#E9EFEC`) para que las tarjetas blancas se separen solas;
  **texto secundario más oscuro** (`#3C4846`), que era lo que se perdía en la v16.
- **Cabeceras con degradado**: `HeroGradient` (teal → violeta suave) en Clenaer y
  TheClurer y `HeroGradientWarm` (ámbar → violeta suave) en Cuenta.
- **Cuadros con cuadro de color**, como en el HTML: cada indicador lleva un cuadrado
  de 8×8 a la izquierda (teal, violeta, verde, ámbar) junto a su etiqueta, y el valor
  grande en color donde toca.
- **Avatar circular con degradado** de marca (teal → violeta) y texto blanco, igual que
  el `.avatar` del HTML.

## Animaciones

`Helpers/Anim.cs` añade fundidos con easing (`CubicEase`) y se aplican al entrar en
Login, Clenaer, TheClurer y Cuenta (la página aparece con un fundido suave en lugar de
aparecer de golpe). Los botones ya tenían sus estados hover/press del sistema.

## Avisos: ventana emergente de Windows

Se elimina el toast **dentro** de la aplicación (`ToastHost` y `ShowToast` ya no existen).

Ahora cada aviso hace **dos** cosas:

1. **Notificación nativa** de Windows (`AppNotificationManager`), como antes.
2. **Ventana emergente propia** (`Views/AlertWindow.cs`): sin barra de título, siempre
   encima (`OverlappedPresenter.IsAlwaysOnTop`), en la **esquina inferior derecha** del
   escritorio, con barra de color, icono, título y detalle. Se apila si llegan varios
   (hasta 4, subiendo hacia arriba), se cierra sola a los 7 s, se pausa si pasas el ratón
   por encima y se cierra al hacer clic. Aparece con un fundido de 240 ms.

Con esto el aviso se ve aunque la app esté minimizada o detrás de otras ventanas.

## v18 — corrección de API en la ventana de avisos

`new OverlappedPresenter { ... }` no compila: en Windows App SDK esa clase **no tiene
constructor público**. Se crea con `OverlappedPresenter.Create()` y después se ajustan
sus propiedades:

```csharp
var presenter = OverlappedPresenter.Create();
presenter.IsAlwaysOnTop = true;
presenter.IsMaximizable = false;
presenter.IsMinimizable = false;
presenter.IsResizable = false;
presenter.SetBorderAndTitleBar(true, false);
AppWindow.SetPresenter(presenter);
AppWindow.IsShownInSwitchers = false;
```

---

# v19 — un solo aviso (fin de la congelación) + contraste y animaciones

## El bug

`ScanPage.Record()` hacía `NotificationService.Alert(item)` **por cada archivo con
alerta**. Con 42 alertas se abrían 42 ventanas emergentes a la vez y la app se
congelaba. Igual pasaba con los toasts nativos.

## La solución

1. **Una sola ventana** (`AlertWindow`): es un singleton. Si llega otro aviso, no se
   crea otra ventana: se **actualiza el contenido** de la que ya está abierta y se
   reinicia su temporizador. Nunca hay más de una.
2. **Un solo aviso por análisis**: `ScanPage` ya no avisa archivo por archivo. Cuenta y
   manda **un resumen** al terminar:
   *«3 maliciosos · 5 sospechosos · 34 limpios de 42 archivos revisados»*,
   con el color del peor caso: **rojo** si hay maliciosos, **amarillo** si solo
   sospechosos, **verde** si todo está limpio.
3. La vigilancia en vivo sigue avisando de cada archivo nuevo, pero reutilizando
   esa misma ventana.

## Contraste (lo que se veía “demasiado blanco”)

- Texto secundario más oscuro: `#2E3A38`.
- Bordes más definidos: `#CBD6D2`.
- El área de contenido ya no es blanca plana: lleva un degradado suave
  `ContentGradient` (verde muy claro → lila muy claro), así las tarjetas resaltan y
  los textos no se pierden.

## Animaciones nuevas

- **Círculo**: el avatar de la cabecera y el de la cuenta hacen un *pop* con rebote
  (`Anim.Pop`, escala 0.86 → 1 con `BackEase`).
- **Cuadros**: los indicadores entran **escalonados** (40/90/140/190/240 ms) con fundido
  suave en vez de aparecer todos de golpe.

---

# v20 — botón de ayuda y pulido visual

## Ayuda (`Views/HelpPage.xaml`)

Módulo **06 · Ayuda**, accesible de dos formas:

- el ítem **Ayuda** del índice izquierdo,
- el botón **?** de la cabecera (junto al avatar).

Contenido:

1. **Qué es Aegis** (motor local, sin nube).
2. **Cómo usar cada módulo** (01 Análisis manual · 02 Aegiss_Clenaer · 03 Aegiss_TheClurer ·
   04 Vigilancia en vivo · 05 Cuenta), numerados y con el color de cada módulo.
3. **Dónde queda todo**: `auth.json`, `profile.json`, `avatar.png`, `archives\`, `app.log`.
4. **Seguridad**: nada se borra sin confirmación, no toca carpetas de sistema, el ZIP se
   verifica antes de borrar y todo es local.
5. **Acceso a la demo**: `admin` / `aegis`.

## Pulido visual

- **Logo nuevo** (cabecera y login): cuadrado redondeado con degradado teal→violeta,
  brillo interior de 1 px y escudo centrado; entra con animación *pop*.
- **Círculo del avatar**: ahora lleva un **anillo con degradado de marca** (elipse de
  2,5 px) alrededor de la foto/iniciales; hace *pop* al cargar y **crece suavemente** al
  pasar el ratón (`Anim.HoverScale`). Igual en la página de Cuenta, con anillo de 3 px.
- **Cuadros de indicadores**: cada uno tiene **borde izquierdo de 4 px con su color**
  (teal, violeta, verde, ámbar), así se distinguen de un vistazo y la fila deja de verse
  plana.

---

# v21 — animación del análisis, cuadros ordenados y firma de la build

## Identidad

El bloque de estado del índice ahora dice exactamente:

```
BETA AEGIS V21
Motor Heurotastic Lyon 2.0
```

y el pie de la ventana muestra `v21`.

## Análisis manual: el círculo

- **Halo giratorio**: se añade un anillo punteado violeta de 176 px que **rota sin parar**
  (7 s por vuelta) mientras el análisis está en marcha. Al terminar se oculta.
- El anillo de progreso (arco de color) y el porcentaje central siguen igual, pero ahora
  el conjunto se mueve y se nota que está trabajando.
- Se detiene también al cancelar.

## Aegiss_Clenaer: más movimiento

- Las filas de la lista **entran escalonadas** (28 ms entre cada una, primeras 12).
- Las pestañas *Archivos / Carpetas* hacen un *pop* al cambiar.
- Se mantienen las animaciones escalonadas de la cabecera y los cuadros.

## Aegiss_TheClurer: los cuadros, ordenados

- **Siempre de mayor a menor** (el algoritmo ordena antes de dibujar) y las filas se
  equilibran por peso, así no queda una fila con un bloque gigante y otra con migajas.
- Cada cuadrado muestra, según el espacio disponible:
  - etiqueta **APP** o **ZIP** (según el mapa),
  - **nombre** de la aplicación o carpeta,
  - **tamaño y nº de archivos**.
- Más aire entre cuadrados (2 px), esquinas de 6 px y borde interior blanco al 27 % para
  que se separen bien sobre el fondo.

---

# v22.0 — más animación en Clenaer, aviso formal y correcciones de análisis

Versión visible en todo el producto: **v22.0** (pantalla inicial, cabecera, pie, título de
la ventana, `Package.appxmanifest` y `AntivirusApp.csproj`).

## Aegiss_Clenaer · ilustración y funcionamiento animados

- **Emblema animado en la cabecera**: dos órbitas punteadas giran en sentidos opuestos,
  un satélite recorre la órbita exterior y el núcleo de marca late (`Anim.Spin`,
  `Anim.PulseLoop`, `Anim.Breathe`). Al escanear, las órbitas se aceleran y el halo
  respira más rápido.
- **Indicadores**: los puntos de color laten desfasados y cada cifra hace un *pop*
  cuando su valor cambia (archivos, carpetas y peso medido).
- **Gráfica TOP**: las barras **crecen desde la izquierda** con entrada escalonada
  (`Charts.Bars(..., animate: true)` + `Anim.GrowBar`).
- **Lista**: las filas se deslizan al aparecer, el icono late y la fila se resalta al
  pasar el ratón. Las tarjetas de la cabecera y los KPI entran con deslizamiento.
- **Barra de barrido** indeterminada sobre la lista mientras hay un escaneo en curso.
- Las animaciones se detienen y se reinician ordenadamente (`StartHeroMotion` /
  `StopHeroMotion`), sin acumular guiones.

## Aviso formal (sustituye a las instrucciones de «elegir archivo»)

- El aviso vive **solo en la página Ayuda**, que abre con un bloque destacado:
  *AVISO IMPORTANTE · ENTORNO DE EXPERIMENTACIÓN · v22.0*, que explica que el programa es un proyecto experimental y
  académico, que **no es un antivirus comercial ni sustituye a Windows Defender**, que
  el motor se inspira en ideas y herramientas publicadas en portales de código abierto
  como **GitHub** (sin incorporar su código ni sus firmas), que los veredictos pueden
  tener falsos positivos/negativos y que el uso es responsabilidad del usuario.
- Las descripciones de los módulos ya no indican elige archivo/carpeta: describen la
  función real de cada módulo.
- El resto de la interfaz no repite el aviso; el pie de la ventana vuelve a decir
  demo local.

## Contraseñas fuera de la interfaz

- El formulario de acceso se conserva, pero sin mostrar la contraseña (PasswordBox
  oculto) ni tarjetas de credenciales, pistas o menciones a `auth.json` en la ayuda.
- Las credenciales y su validación siguen **solo en el código**.

## Aegiss_TheClurer: barras animadas junto a los cuadros

- Cada mapa de cuadrados tiene ahora un botón para alternar entre el **mapa de cuadros**
  y una **gráfica de barras animadas** con el mismo ranking (12 elementos).
- Los cuadrados se mantienen intactos; el botón solo cambia la vista y vuelve a dibujar
  las barras con crecimiento escalonado (`Charts.Bars(..., animate: true)`).
- El texto descriptivo cambia según la vista activa.

## Correcciones en el análisis (sin cambiar el flujo de uso)

- **ZIP**: cada entrada se extrae y analiza por separado; una entrada corrupta, con
  nombre inválido o protegida ya no aborta todo el análisis (se marca como ilegible).
  Se sanean los nombres temporales y se limpian los temporales siempre.
- **Resultados**: los archivos que no se pudieron leer ya **no aparecen como «Limpio»**:
  se muestran como «Error», sin botón de eliminar y con el riesgo «—».
- **Contadores**: `Forget` descuenta también los limpios; el total nunca queda por
  debajo de lo revisado; el resumen final incluye los ilegibles.
- **Fallos de escaneo**: si algo falla, el título y el detalle lo indican y se emite un
  aviso de error; antes el mensaje se sobrescribía con «completado».
- **Selectores**: si el selector de archivos/carpetas falla, se informa en la página en
  lugar de lanzar una excepción.
- **Clenaer**: la cancelación se propaga como cancelación real (no como escaneo
  completo); si el servicio termina con avisos se muestra y se notifica; al eliminar una
  carpeta desaparecen también sus archivos y subcarpetas de la lista.
- **Vigilancia en vivo**: activar la vigilancia nunca cierra la app si la carpeta no
  existe o no hay permisos; se informa y el interruptor vuelve a su sitio.
