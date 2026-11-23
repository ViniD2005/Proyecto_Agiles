# Guía de instalación para el equipo (desde cero, Windows)

Esta guía es para quien **acaba de clonar el repositorio y no tiene nada instalado**. Al terminar tendrás EduMonitor AI funcionando en tu computador: login, materias de Moodle, dashboard, agente de IA, recomendaciones, reportes y auditoría.

> **Regla de oro:** todo corre en Docker. Para **ejecutar** el sistema solo necesitas **Docker Desktop y Git**. .NET y Node solo hacen falta si vas a programar (sección 10).

| | |
|---|---|
| Tiempo total | 45 a 90 min la primera vez (casi todo es descarga) |
| Descarga | unos 10 GB (imágenes de Docker + modelo de IA) |
| Disco libre | 20 GB |
| Memoria RAM | **16 GB recomendado** (Docker usa 8 GB). Con 8 GB: lee la nota de la sección 1.4 |
| Sistema | Windows 10 (22H2) o Windows 11, 64 bits |

Todos los comandos de esta guía se escriben en **PowerShell** (menú Inicio → «PowerShell»). Cuando diga «como administrador», clic derecho → *Ejecutar como administrador*.

---

## 1. Preparar Windows

### 1.1 Activar la virtualización

1. Abre el **Administrador de tareas** (`Ctrl + Shift + Esc`) → pestaña **Rendimiento** → **CPU**.
2. Abajo a la derecha debe decir **Virtualización: Habilitada**.
3. Si dice *Deshabilitada*: reinicia el equipo, entra a la BIOS/UEFI (suele ser `F2`, `F10`, `Supr` o `Esc` al encender) y activa **Intel VT-x** o **AMD SVM / Virtualización**. Guarda y arranca Windows.

### 1.2 Instalar WSL 2

En PowerShell **como administrador**:

```powershell
wsl --install
```

**Reinicia el equipo** cuando lo pida. Después de reiniciar, si se abre una ventana de Ubuntu pidiendo usuario y contraseña, completa lo que pide (es solo para WSL) y ciérrala.

**Control:**

```powershell
wsl --status
```

Debe mostrar «Versión predeterminada: 2». Si dice que falta una actualización: `wsl --update`.

### 1.3 Instalar Docker Desktop

1. Descarga **Docker Desktop for Windows** desde https://www.docker.com/products/docker-desktop/ e instálalo dejando marcada la opción **Use WSL 2 instead of Hyper-V**.
2. Reinicia si lo pide, abre **Docker Desktop**, acepta los términos (no hace falta crear cuenta; puedes omitir el inicio de sesión).
3. Espera a que abajo a la izquierda diga **Engine running** (en verde).

**Control:**

```powershell
docker --version
docker compose version
docker run --rm hello-world
```

Los tres deben funcionar; el último imprime «Hello from Docker!».

### 1.4 Darle memoria a Docker

Sin esto, SQL Server o Moodle se cierran solos por falta de memoria.

1. Crea (o edita) el archivo `C:\Users\TU_USUARIO\.wslconfig` con este contenido:

   ```ini
   [wsl2]
   memory=8GB
   processors=4
   ```

2. Aplica el cambio:

   ```powershell
   wsl --shutdown
   ```

3. Abre de nuevo Docker Desktop y espera **Engine running**.

> **Si tu equipo tiene solo 8 GB de RAM:** pon `memory=5GB`, **no** levantes el perfil `ia` (Ollama) y en `.env` usa un modelo en la nube gratuito (`LLM_PROVIDER=Gemini` o `Groq` con su `LLM_API_KEY`; ver sección 6). Todo lo demás funciona igual.

### 1.5 Instalar Git

Descarga **Git for Windows** desde https://git-scm.com/download/win e instálalo con las opciones por defecto.

**Control:** `git --version`

---

## 2. Clonar el repositorio

Elige una carpeta corta, sin espacios ni tildes (por ejemplo `C:\dev`):

```powershell
mkdir C:\dev
cd C:\dev
git clone https://github.com/ViniD2005/Proyecto_Agiles.git edumonitor-ai
cd edumonitor-ai
```

- Si el repositorio es privado y pide credenciales: primero pide a Vinicio que te agregue como colaborador, y inicia sesión con tu cuenta de GitHub en la ventana que se abre.
- La rama `main` ya contiene los Sprints 1, 2 y 3.

**Control:** `dir` debe mostrar `docker-compose.yml`, `README.md`, las carpetas `src`, `frontend`, `docs` y `scripts`.

---

## 3. Crear el archivo `.env` (contraseñas y configuración)

El `.env` **no está en GitHub a propósito** (tiene claves). Hay dos formas de obtenerlo:

**Opción A (recomendada): Vinicio te pasa su `.env` por mensaje privado.** Cópialo en `C:\dev\edumonitor-ai\.env`. Así tendrás el mismo token de Moodle y las mismas claves que él, y la restauración de datos de la sección 5 funciona sin cambios.

**Opción B: crearlo desde la plantilla** (si empiezas Moodle desde cero):

```powershell
copy .env.example .env
notepad .env
```

Revisa estos valores:

| Variable | Qué poner |
|---|---|
| `MSSQL_SA_PASSWORD` | Mínimo 8 caracteres con mayúscula, minúscula, número y símbolo. La de la plantilla sirve |
| `JWT_KEY` | Mínimo 32 caracteres |
| `LLM_BASE_URL` | `http://ollama:11434` (Ollama dentro de Docker, lo recomendado) |
| `MOODLE_TOKEN` | Déjalo así por ahora; se llena en la sección 5, ruta B |
| `SEED_DEFAULT_PASSWORD` | Contraseña de los usuarios de prueba (ver sección 7) |

> **Nunca subas el `.env` a GitHub.** Ya está en `.gitignore`; comprueba con `git status` que no aparezca.

---

## 4. Comprobar que los puertos están libres

EduMonitor usa estos puertos de tu equipo:

| Puerto | Servicio |
|---|---|
| 5174 | Aplicación web |
| 5000 · 5001 · 5002 · 5003 | Gateway y microservicios |
| 1433 | SQL Server |
| 5672 · 15672 | RabbitMQ |
| 8081 | Moodle |
| 11434 | Ollama |
| 8025 · 1025 | Mailpit (correo de pruebas) |

```powershell
netstat -ano | findstr ":5174 :5000 :5001 :5002 :5003 :1433 :8081 :11434 :8025 :5672"
```

**Control:** no debe imprimir ninguna línea con `LISTENING`. Si aparece alguna, otro programa usa ese puerto:

- **Puerto 1433:** si tienes SQL Server instalado en Windows, cámbialo en `docker-compose.yml` de `"1433:1433"` a `"1434:1433"` (solo cambia el número de la izquierda; los servicios se conectan por dentro y no se afectan).
- **Puerto 5174:** cámbialo igual en el servicio `frontend` (`"5175:80"`) **y** en el servicio `gateway` cambia `Cors__Origins__0` a `http://localhost:5175`.
- Cualquier otro: cambia el número de la izquierda de ese servicio.

---

## 5. Levantar el sistema y cargar los datos

Moodle guarda sus cursos, usuarios y notas en una base de datos que **no viaja con Git**. Hay dos rutas. **Usa la A si Vinicio te comparte el respaldo** (10 minutos); la B es para empezar desde cero (1 a 2 horas).

### Ruta A (recomendada): restaurar el respaldo de Vinicio

El resultado es un sistema **idéntico al suyo**: usuarios, materias, estructura temática, notas, recomendaciones y Moodle ya configurado.

1. Descarga la carpeta de respaldo que Vinicio comparte (contiene 4 archivos: `sqlserver_data.tgz`, `mariadb_data.tgz`, `moodle_data.tgz`, `moodledata_data.tgz`). Descomprímela como `C:\dev\respaldo-edumonitor` (junto a la carpeta del repositorio, no dentro).
2. Asegúrate de tener el `.env` de Vinicio en la raíz del repositorio (sección 3, opción A).
3. Con Docker Desktop abierto, desde `C:\dev\edumonitor-ai`:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\restaurar-datos.ps1
   ```

   Si guardaste el respaldo en otra carpeta: `... -File .\scripts\restaurar-datos.ps1 -Carpeta D:\mi\ruta`.
4. Escribe `SI` cuando lo pida. Descargará imágenes (varios GB) y restaurará los 4 volúmenes.
5. Enciende todo (la primera vez tarda 10 a 20 minutos porque construye los servicios):

   ```powershell
   docker compose --profile ia --profile correo up -d --build
   ```

**Control:** al terminar,

```powershell
docker ps
```

deben aparecer **11 contenedores** `Up`: `em-sqlserver`, `em-rabbitmq`, `em-mariadb`, `em-moodle`, `em-ollama`, `em-mailpit`, `em-identity`, `em-academic`, `em-analytics`, `em-gateway`, `em-frontend`. `em-sqlserver` y `em-rabbitmq` deben decir **(healthy)**. Ninguno debe decir `Restarting`.

Salta a la sección 6.

### Ruta B: desde cero (sin respaldo)

1. Enciende todo:

   ```powershell
   docker compose --profile ia --profile correo up -d --build
   ```

   Comprueba los 11 contenedores como en la ruta A.
2. Espera 5 a 10 minutos a que Moodle termine de instalarse la primera vez (`docker compose logs -f moodle`, sal con `Ctrl + C`). Debe abrir `http://localhost:8081`.
3. Configura Moodle siguiendo **`docs/GUIA_MOODLE.md`** paso a paso: cursos, categorías 40/30/30, actividades, usuarios y notas (con los CSV de la carpeta `moodle/`) y el **token** del servicio web.
4. Copia el token en `.env` (`MOODLE_TOKEN=...`) y reinicia el servicio académico:

   ```powershell
   docker compose up -d academic-api
   ```
5. Entra a la aplicación como docente1 (sección 7), abre **Mis materias** y pulsa el botón de **sincronizar** para traer las materias, estudiantes y notas desde Moodle.
6. La **estructura temática** y el **mapeo de actividades** no vienen de Moodle: se crean desde **Estructura temática** (puedes importar el JSON de ejemplo o usar el PDF del sílabo).

---

## 6. Descargar el modelo de IA (una sola vez)

El agente usa el modelo `qwen2.5:3b` (unos 2 GB), que corre dentro de Docker:

```powershell
docker exec em-ollama ollama pull qwen2.5:3b
```

Después, **precalienta** el modelo para que la primera consulta no tarde:

```powershell
docker exec em-ollama ollama run qwen2.5:3b "responde solo: listo"
```

- Sin Ollama el sistema funciona igual, pero el agente responde solo por reglas, sin el texto del modelo.
- Alternativa gratuita en la nube (para equipos con poca memoria): en `.env` pon `LLM_PROVIDER=Gemini` (o `Groq`), `LLM_MODEL=` con el nombre del modelo y `LLM_API_KEY=` con tu clave gratuita; luego `docker compose up -d analytics-api`. Nota: con estos proveedores el modelo también recibe solo códigos anónimos (E01, E02…), nunca nombres.
- La primera consulta del agente puede tardar 1 a 2 minutos en equipos sin GPU; las siguientes, entre 20 y 60 s.

---

## 7. Entrar y probar que todo funciona

Abre **http://localhost:5174**.

| Usuario | Rol | Contraseña |
|---|---|---|
| `docente1@uta.edu.ec` | Docente (dueño de las materias de prueba) | el valor de `SEED_DEFAULT_PASSWORD` de tu `.env` |
| `docente2@uta.edu.ec` | Docente sin materias (prueba de permisos) | igual |
| `admin@edumonitor.local` | Administrador técnico (solo ve la auditoría) | igual |

Moodle: http://localhost:8081, usuario y contraseña `MOODLE_ADMIN_USER` / `MOODLE_ADMIN_PASSWORD` del `.env`.

**Lista de comprobación (5 minutos):**

| # | Qué hacer | Resultado esperado |
|---|---|---|
| 1 | Abrir http://localhost:5000/health | Responde OK |
| 2 | Entrar como `docente1` | Ves **Mis materias** con Programación Web y Base de Datos |
| 3 | **Dashboard** → elegir una materia | Promedio, niveles de riesgo y tabla de estudiantes |
| 4 | Clic en un estudiante | Ficha con notas, temas y gráfica de **tendencia** |
| 5 | **Agente IA** | Indicador verde «Ollama · qwen2.5:3b disponible»; al preguntar «¿Qué estudiantes están en riesgo y por qué?» responde con Hechos, Inferencias, Recomendaciones e Incertidumbre |
| 6 | **Reportes** → *Generar reporte* → *Descargar PDF* | Vista previa y PDF con las mismas cifras del dashboard |
| 7 | Cerrar sesión y entrar como `admin@edumonitor.local` | **Auditoría** muestra los eventos; no hay tokens ni contraseñas |
| 8 | http://localhost:8025 (Mailpit) | Bandeja de correo de pruebas (se llena al enviar una recomendación aprobada) |

Si los 8 pasos funcionan, tu instalación está completa.

---

## 8. Uso diario: apagar y prender correctamente

**Apagar** (siempre así; no cierres Docker Desktop ni apagues el equipo con los contenedores corriendo):

```powershell
cd C:\dev\edumonitor-ai
docker compose --profile ia --profile correo stop
docker ps
```

`docker ps` debe salir vacío. Después ya puedes cerrar Docker Desktop.

**Prender:**

1. Abre Docker Desktop y espera **Engine running**.
2. Bases de datos y mensajería primero:

   ```powershell
   docker compose up -d sqlserver rabbitmq mariadb
   ```

   Espera a que `docker ps` muestre **(healthy)** en `em-sqlserver` y `em-rabbitmq`.
3. Moodle y el resto:

   ```powershell
   docker compose up -d moodle
   docker compose --profile ia --profile correo up -d
   ```
4. Precalienta el modelo si vas a usar el agente: `docker exec em-ollama ollama run qwen2.5:3b "responde solo: listo"`.

**Actualizar cuando haya cambios nuevos en el repositorio:**

```powershell
git pull origin main
docker compose --profile ia --profile correo up -d --build
```

---

## 9. Problemas frecuentes

| Síntoma | Causa y solución |
|---|---|
| `error during connect` / `cannot connect to the Docker daemon` | Docker Desktop no está abierto. Ábrelo y espera **Engine running** |
| Docker dice «WSL 2 installation is incomplete» | `wsl --update` en PowerShell como administrador y reinicia |
| Docker dice «Virtualization is disabled» | Actívala en la BIOS (sección 1.1) |
| `em-sqlserver` sale con `Exited (1)` | La contraseña `MSSQL_SA_PASSWORD` no cumple la política (8+ caracteres con mayúscula, minúscula, número y símbolo). Corrígela en `.env`. **Si ya existían datos** de otra contraseña, usa la del `.env` de Vinicio |
| `em-sqlserver` o `em-moodle` con `Exited (137)` | Falta memoria. Revisa la sección 1.4 y sube `memory` en `.wslconfig` |
| `port is already allocated` / `address already in use` | Otro programa usa el puerto. Sección 4 |
| `pull access denied` o error al descargar imágenes de `bitnamilegacy` | Revisa tu internet y vuelve a ejecutar el mismo comando. Si persiste, consulta «Imagen alternativa» en `docs/GUIA_MOODLE.md` |
| La construcción (`--build`) falla descargando paquetes | Es la red. Reintenta; si usas una red de la universidad con proxy, prueba con datos móviles |
| La web abre pero el login da error o 502/504 | Los servicios aún arrancan. Espera 30 s y recarga. Si sigue: `docker logs em-gateway --tail 40` |
| El login dice credenciales inválidas | Usa la contraseña `SEED_DEFAULT_PASSWORD` de tu `.env`. Si cambiaste ese valor después del primer arranque, los usuarios ya creados conservan la anterior |
| «Mis materias» vacío | Falta sincronizar con Moodle (ruta B, paso 5) o el `MOODLE_TOKEN` de `.env` es incorrecto (`docker logs em-academic --tail 40`) |
| El agente dice «Ollama no disponible» | Falta el perfil `ia` (`docker compose --profile ia up -d`), `LLM_BASE_URL` no es `http://ollama:11434`, o no descargaste el modelo (sección 6) |
| El agente responde con «Error 504» | La primera consulta con el modelo frío tarda. Precalienta el modelo (sección 6) y repite |
| El correo no llega | Mailpit corre con el perfil `correo`; la bandeja está en http://localhost:8025. Solo se envía una recomendación **aprobada** |
| Un script `.ps1` dice que la ejecución está deshabilitada | Ejecútalo como: `powershell -ExecutionPolicy Bypass -File .\scripts\NOMBRE.ps1` |
| Una vez instalado todo, quiero empezar de cero con las bases de EduMonitor | `powershell -ExecutionPolicy Bypass -File .\scripts\reset-databases.ps1` (no toca Moodle) |

Si nada de esto resuelve tu caso, copia el mensaje de error **completo** y la salida de `docker ps` y `docker logs <contenedor> --tail 40` y compártelos con el equipo.

---

## 10. Para programar (opcional)

No necesitas esto para ejecutar el sistema. Solo si vas a modificar código:

| Herramienta | Para qué | Dónde |
|---|---|---|
| .NET 8 SDK | Compilar y correr las pruebas (`dotnet test`) | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Node 20 LTS | Frontend en modo desarrollo (`npm run dev`) | https://nodejs.org |
| Visual Studio Code | Editor | https://code.visualstudio.com |

```powershell
dotnet --version        # debe empezar con 8.
node --version          # debe empezar con v20.
dotnet test EduMonitor.sln
```

Flujo de trabajo del equipo (GitFlow): crea tu rama desde `develop` (`feature/nombre-de-la-tarea`), haz commits pequeños, sube la rama y abre un Pull Request hacia `develop`.

---

## 11. Para quien comparte los datos (Vinicio u otro integrante con el sistema ya configurado)

Para que un compañero tenga una copia idéntica sin configurar Moodle:

1. Con Docker Desktop abierto, desde la raíz del repositorio:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\respaldar-datos.ps1
   ```

   Apaga el sistema y genera 4 archivos `.tgz` en `..\respaldo-edumonitor` (fuera del repositorio, para no subirlos a Git por error).
2. Comprime esa carpeta en un `.zip` y compártela por Drive o similar.
3. Pasa tu archivo **`.env`** por un canal **privado** (mensaje directo). **Nunca** por GitHub.
4. Vuelve a encender tu sistema: `docker compose --profile ia --profile correo up -d`.

El respaldo incluye los datos de SQL Server (usuarios, materias, estructura temática, recomendaciones, auditoría), MariaDB y Moodle. No incluye el modelo de IA, que cada quien descarga (sección 6).

---

## 12. Reglas para no dañar nada

| No hagas esto | Qué pasa |
|---|---|
| `docker compose down -v` | **Borra todos los volúmenes**: bases de datos, Moodle y modelo. Es irreversible |
| `docker volume prune` / `docker system prune --volumes` | Lo mismo: borra datos |
| Apagar el equipo o cerrar Docker Desktop con los contenedores corriendo | Puede corromper SQL Server o MariaDB. Usa siempre `stop` (sección 8) |
| Subir el `.env` o un respaldo `.tgz` a GitHub | Expone claves y datos del curso |
| Usar el sistema con cursos reales | Es un entorno de **prueba** con datos ficticios |
