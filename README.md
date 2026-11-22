# Nebula Optimizer

Optimizador local de Windows con interfaz en español, transiciones suaves, identidad visual propia, mantenimiento programable y una ficha técnica .env por perfil.

## Inicio rápido

1. Descomprime el ZIP en una carpeta local.
2. Abre Iniciar.bat.
3. Si el ZIP incluye NebulaOptimizer.exe, el BAT lo ejecuta directamente. Si no, compila y ejecuta el proyecto con .NET 10 SDK.
4. En Automático, elige frecuencia y hora, activa la rutina y guarda. Este paso crea una tarea del Programador de tareas para tu usuario.
5. En Ajustes, cambia el nombre del perfil si quieres un archivo .env con un nombre propio.

El ZIP incluye un ejecutable autónomo para Windows x64; no necesita el SDK para abrirlo. El SDK solo se usa como alternativa si falta el ejecutable.

El proyecto apunta a .NET 10 LTS. Para compilar desde el código fuente se requiere Windows y .NET 10 SDK con soporte de escritorio de Windows.

## Mantenimiento automático

Puedes programar una ejecución diaria, cada 3 días, semanal o cada 14 días. La tarea corre con tu usuario y solo cuando has iniciado sesión y el equipo está encendido. Al activar la rutina, autorizas la limpieza periódica sin una confirmación en cada ejecución.

Cada ejecución elimina archivos sueltos de la carpeta temporal de tu usuario que tengan más de 48 horas. No entra en subcarpetas ni toca Descargas o documentos.

De forma opcional, la rutina cambia de Alto rendimiento al plan Equilibrado de Windows ahora y en cada ejecución. Puede reducir consumo en hardware compatible y afectar el rendimiento. Conserva los planes de ahorro y personalizados; al desactivar la opción, restaura el plan anterior solo si Equilibrado sigue activo. Si Windows no autoriza el cambio, la limpieza sigue programada y el resultado lo indica. No cierra procesos ni modifica controladores. El control de una GPU dedicada depende de su fabricante.

No hay un ajuste universal seguro que baje el uso real de CPU/GPU sin cerrar aplicaciones o cambiar controladores y opciones específicas del equipo. El plan Equilibrado puede limitar consumo en algunos equipos, pero no garantiza menor uso de GPU.

## Limpieza manual

La limpieza manual muestra cantidad y tamaño estimado, exige una casilla y una confirmación final. Omite enlaces, archivos recientes y bloqueados. No necesita privilegios de administrador.

No cambia servicios, registro, inicio de Windows ni configuraciones de red.

## Perfil .env

El archivo se crea bajo:

    %LOCALAPPDATA%\NebulaOptimizer\<nombre>.env

Incluye nombre del equipo, Windows, arquitectura, modelo del procesador, procesadores lógicos, memoria física, capacidad/espacio libre de discos fijos y fecha de captura. No guarda contraseñas, claves de producto, números de serie ni direcciones MAC. Es texto legible: revísalo antes de compartirlo. La carpeta y el archivo reciben permisos restringidos al usuario actual cuando Windows lo permite.

Al cambiar el nombre, el archivo de perfil anterior se conserva. La captura es local y no hay telemetría ni conexiones de red en esta versión.

## Estructura

- Program.cs: interfaz, captura de datos, limpieza delimitada, perfil .env y comprobaciones.
- NebulaOptimizer.csproj: proyecto WinForms sin paquetes externos.
- app.manifest: declara ejecución como usuario estándar, sin elevación.
- Iniciar.bat: abre el ejecutable incluido o, si falta, ejecuta el proyecto con .NET 10 SDK.
- Assets/NebulaMark.svg y Assets/NebulaWordmark.svg: marca vectorial.
- Assets/NebulaOptimizer.ico: icono del ejecutable.
- SECURITY.md: límites y manejo de datos.

La aplicación anima la entrada y el cambio de páginas con transiciones breves, e incluye una marca dibujada de forma nítida para la interfaz y un icono propio.

## Proporción de código

La proporción solicitada se calcula por bytes de los archivos de código .cs y .bat incluidos en la fuente, excluyendo documentación, configuración y el ejecutable: C# 98.7 % y Batchfile 1.3 % (redondeado a una décima).

## Instalación:

-descargue este ZIP en su explorador de archivos, después, tendrá que hacer los pasos de instalación ya mencionados. [![Descargar NebulaOptimizer (ZIP)](./outputs/NebulaOptimizer.zip)


