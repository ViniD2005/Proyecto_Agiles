# EduMonitor AI

Middleware docente con agente de IA para el seguimiento académico personalizado sobre Moodle.
Universidad Técnica de Ambato · FISEI · Carrera de Software · Metodologías Ágiles · Cuarto "B".

Equipo: Vinicio Arias (Scrum Master / backend) · Damian Bastidas (Moodle / DevOps) · Mateo Morales (frontend) · Jostin Sigcha (analítica / IA).
Product Owner: Ing. Hernán Naranjo.

> El sistema **no califica, no modifica notas en Moodle** y no sustituye la valoración del docente.

## Estado: Sprint 3 · Agente de IA, recomendaciones, reportes y auditoría

Sprint 1 (HU-01 a HU-06) y Sprint 2 (HU-07 a HU-12) están descritos en `docs/GUIA_MOODLE.md` y `docs/SPRINT2.md`. Este sprint agrega:

| Historia | Descripción | Dónde |
|---|---|---|
| HU-13 | Agente con 6 herramientas controladas; no inventa datos | `Analytics/Agente/` |
| HU-14 | Respuesta con hechos, inferencias, recomendaciones e incertidumbre (M9) | `pages/AgenteIA.jsx` |
| HU-15 | Recomendaciones por estudiante: editar y aprobar | `Analytics/Recomendaciones/` |
| HU-16 | Reporte inteligente del curso en PDF (M10) | `Analytics/Reportes/` |
| HU-17 | Auditoría centralizada sin secretos (solo administrador) | `Common/Auditoria/`, `pages/Auditoria.jsx` |
| HU-18 | Documentación, Postman y cierre | `docs/SPRINT3.md`, `postman/` |
| HU-19 · HU-20 · HU-21 | Opcionales: tendencia y proyección · envío por correo · sílabo en PDF | ver `docs/SPRINT3.md` |

**Guía completa del Sprint 3: `docs/SPRINT3.md`** (escenarios, endpoints, esquema de BD, matriz de requisitos y qué se verificó).

### Inteligencia artificial local (opcional pero recomendada)

El agente usa **Ollama** (gratis, local). Los cálculos los hace el código; el modelo solo redacta, y solo ve códigos anónimos (E01, E02…). Sin Ollama el sistema sigue funcionando con respuestas por reglas.

```powershell
# Ollama instalado en el equipo (Windows): descargar el modelo una vez
ollama pull qwen2.5:3b
# o Ollama dentro de Docker:
docker compose --profile ia up -d
docker exec em-ollama ollama pull qwen2.5:3b     # y en .env:  LLM_BASE_URL=http://ollama:11434
# Correo de pruebas (Mailpit, bandeja en http://localhost:8025)
docker compose --profile correo up -d
```

Proveedor alterno gratuito: `LLM_PROVIDER=Gemini` o `Groq` con `LLM_API_KEY` en `.env`.

## Estado del Sprint 1 · Fundaciones e integración con Moodle

| Historia | Descripción | Estado en el código |
|---|---|---|
| HU-01 | Moodle propio con datos de prueba | `docker-compose.yml` + `moodle/*.csv` + `docs/GUIA_MOODLE.md` |
| HU-02 | Servicios web REST y token limitado | Guía T2.1 + colección Postman (carpeta «Moodle directo») |
| HU-03 | 3 microservicios + Gateway + SQL Server + RabbitMQ | Solución .NET 8, Dockerfiles, `/swagger` y `/health` |
| HU-04 | Login del docente con roles | MS-A: BCrypt + JWT; Gateway: políticas por rol; React: login y rutas protegidas |
| HU-05 | Materias asignadas desde Moodle | MS-B: `MoodleClient` + sincronización; pantalla «Mis materias» (M2) |
| HU-06 | Estudiantes y actividades | MS-B: endpoints paginados con autorización por curso; pantalla M3 |

## Arquitectura

```
React (SPA :5174) ──HTTPS/REST──► API Gateway YARP (:5000)  ── valida JWT y rol
                                     ├── /api/auth, /api/auditoria ─► MS-A Identidad y Auditoría (:5001) ─► DB_Identidad
                                     ├── /api/materias, /api/moodle ─► MS-B Académico (:5002) ─► DB_Academico
                                     │                                     └── API REST de Moodle (solo lectura) ─► Moodle Docker (:8081)
                                     └── /api/analitica, /api/agente, /api/reportes, …/recomendaciones, …/reporte, …/silabo
                                                                       ─► MS-C Analítica y Agente IA (:5003) ─► DB_Analitica ─► Ollama (:11434)
                     RabbitMQ (:5672 / panel :15672) — eventos CalificacionesSincronizadas y MapeoActividadActualizado (Sprint 2)
```

```
EduMonitor.sln
src/
  BuildingBlocks/EduMonitor.Common   JWT, políticas por rol y Swagger compartidos
  Gateway/EduMonitor.Gateway         YARP (rutas en appsettings.json)
  Services/Identity/...Identity.Api  MS-A · Usuario, Rol, RegistroAuditoria
  Services/Academic/...Academic.Api  MS-B · Materia, Estudiante, Matricula, Actividad, SincronizacionLog
  Services/Analytics/...Analytics.Api MS-C · esqueleto (Sprints 2 y 3)
tests/                               xUnit (login/token/rol, clasificación, permisos)
frontend/                            React + Vite
moodle/                              CSV de cursos, usuarios y notas ficticias
postman/                             Colección de pruebas del Sprint 1
docs/GUIA_MOODLE.md                  Paso a paso de Moodle y servicios web
```

## Puesta en marcha (un solo comando)

Requisitos: Docker Desktop (8 GB de RAM asignados recomendado), Git. Para desarrollo local: .NET 8 SDK y Node 20.

```bash
git clone https://github.com/<organizacion>/edumonitor-ai.git
cd edumonitor-ai
cp .env.example .env            # completar contraseñas y, luego, MOODLE_TOKEN
docker compose up -d --build
```

| Servicio | URL |
|---|---|
| Frontend | http://localhost:5174 |
| API Gateway | http://localhost:5000/health |
| MS-A Swagger | http://localhost:5001/swagger |
| MS-B Swagger | http://localhost:5002/swagger |
| MS-C Swagger | http://localhost:5003/swagger |
| Moodle | http://localhost:8081 |
| Mailpit (perfil `correo`) | http://localhost:8025 |
| RabbitMQ | http://localhost:15672 |

La primera vez configurar Moodle siguiendo **`docs/GUIA_MOODLE.md`** (cursos, 40/30/30, actividades, usuarios, notas y token). Luego pegar el token en `.env` y ejecutar `docker compose up -d academic-api`.

### Usuarios de prueba (EduMonitor)

| Correo | Rol | Uso |
|---|---|---|
| docente1@uta.edu.ec | Docente | Dueño de PW-401-B y BD-402-B |
| docente2@uta.edu.ec | Docente | Sin materias: prueba de permisos |
| admin@edumonitor.local | AdministradorTecnico | No ve materias; consulta la auditoría |

Contraseña: el valor de `SEED_DEFAULT_PASSWORD` del `.env`.

## Desarrollo local sin Docker para el código

```bash
docker compose up -d sqlserver rabbitmq mariadb moodle
# Backend (en terminales separadas); exportar Jwt__Key, ConnectionStrings__Default, Moodle__Token...
dotnet run --project src/Services/Identity/EduMonitor.Identity.Api --urls http://localhost:5001
dotnet run --project src/Services/Academic/EduMonitor.Academic.Api --urls http://localhost:5002
dotnet run --project src/Services/Analytics/EduMonitor.Analytics.Api --urls http://localhost:5003
dotnet run --project src/Gateway/EduMonitor.Gateway --urls http://localhost:5000
# Frontend
cd frontend && npm install && npm run dev
```

## Pruebas

```bash
dotnet test EduMonitor.sln
```

- `EduMonitor.Identity.Tests`: login correcto/incorrecto, token con rol y expiración, validación del formulario (T4.6).
- `EduMonitor.Academic.Tests`: clasificación de actividades y pesos 40/30/30; un docente no accede a la materia de otro (T6.5).
- `EduMonitor.Analytics.Tests` (Sprint 3): agente, anonimización, tendencia, recomendaciones, sílabo y sanitizador de auditoría.
- Postman: `postman/EduMonitor.postman_collection.json` (Sprint 1), `EduMonitor.Sprint2…` y `EduMonitor.Sprint3.postman_collection.json` (agente, recomendaciones, reporte, auditoría y permisos).

## Flujo de trabajo (GitFlow)

- `main`: versión estable que se demuestra en la Sprint Review.
- `develop`: integración. Todo entra por **Pull Request revisado por otro integrante**.
- `feature/HU-xx-descripcion`: una rama por historia o tarea. Ej.: `feature/HU-04-login`.

```bash
git checkout develop && git pull
git checkout -b feature/HU-05-mis-materias
git commit -m "feat(HU-05): sincronizar materias del docente desde Moodle"
git push -u origin feature/HU-05-mis-materias   # abrir PR hacia develop
```

Convención de commits: `feat`, `fix`, `test`, `docs`, `chore` + `(HU-xx)`.

## Notas técnicas

- **Base de datos:** en el Sprint 1 cada servicio crea su base con `EnsureCreated()`. Cuando el modelo se estabilice:
  `dotnet ef migrations add Inicial --project src/Services/Academic/EduMonitor.Academic.Api` y reemplazar por `Database.Migrate()`.
- **Secretos:** solo en `.env` (no versionado). El token de Moodle y la clave JWT nunca se escriben en el código.
- **Moodle de solo lectura (RN-06):** el token solo tiene funciones de consulta.
- **Clasificación de actividades:** en el Sprint 1 se hace por el nombre de la actividad (`ActivityClassifier`). En el Sprint 2 se contrasta con las categorías del libro de calificaciones.
