# Seguridad y privacidad

- La aplicación se ejecuta como usuario estándar (asInvoker) y no solicita elevación.
- El perfil se escribe en %LOCALAPPDATA%\NebulaOptimizer; intenta aplicar una ACL protegida para el SID del usuario actual al directorio y al .env.
- El perfil contiene información técnica e identificadora del dispositivo, incluido el nombre del equipo. Es texto sin cifrar.
- No captura usuario de Windows, números de serie, direcciones MAC ni claves de producto. No transmite los datos por red.
- La limpieza trabaja solo con archivos del nivel superior de %TEMP% dentro del perfil del usuario; el umbral es 48 horas. No sigue enlaces y vuelve a comprobar ruta, antigüedad y atributos justo antes de borrar.
- La limpieza manual requiere revisar la estimación y confirmar. Al habilitar la tarea programada, autorizas que ese mismo alcance se limpie automáticamente en cada ejecución. Los archivos bloqueados se omiten.
- La tarea programada pertenece al usuario actual, no pide contraseña ni elevación y solo funciona con la sesión iniciada. Desactívala desde la página Automático antes de mover o quitar el ejecutable.
- El plan Equilibrado es opcional. Solo cambia desde Alto rendimiento; conserva planes de ahorro o personalizados. Puede reducir consumo en parte del hardware y afectar rendimiento, pero no controla de manera universal las GPU dedicadas. Al desactivarlo, restaura el plan previo únicamente si Equilibrado sigue activo.
- El programa no finaliza aplicaciones ni cambia controladores, servicios, registro, planes de inicio o configuraciones de red.
- Esta herramienta no sustituye un antivirus ni una auditoría de seguridad de Windows.

Para informar un problema, incluye la versión de Windows y los pasos para reproducirlo. No adjuntes el .env sin revisar el contenido.
