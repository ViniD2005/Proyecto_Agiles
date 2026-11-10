# Sprint 2 · Planificación académica y analítica

**Objetivo:** el docente relaciona cada actividad con sus contenidos mínimos y ve qué estudiantes están en riesgo y en qué temas fallan.
**Alcance:** 6 historias (HU-07 a HU-12) · 26 SP · 27 tareas · 101 h.

Este paquete contiene **solo lo del Sprint 2** (archivos nuevos y los pocos modificados). Se copia sobre el repositorio del Sprint 1.

---

## 1. Qué se entrega, por historia y tarea

| Historia | SP | Qué hace | Tareas (resp. según el plan) |
|---|---|---|---|
| **HU-07** Sincronización de calificaciones | 5 | Copia las notas de Moodle (0-10) distinguiendo **nota cero**, **sin entrega** y **pendiente de calificar**; guarda la bitácora y publica `CalificacionesSincronizadas` | T7.1 Damian · T7.2 Damian · T7.3 Damian · T7.4 Vinicio · T7.5 Damian · T7.6 Vinicio |
| **HU-08** Estructura temática | 5 | CRUD de unidades → temas → contenidos mínimos, con confirmación al borrar lo asignado e importación del sílabo (JSON) | T8.1 Vinicio · T8.2 Vinicio · T8.3 Mateo · T8.4 Damian |
| **HU-09** Mapeo actividad ↔ contenidos | 5 | Relación N:M, actividades «pendientes», evento `MapeoActividadActualizado`; el mapeo se conserva al re-sincronizar | T9.1 Vinicio · T9.2 Vinicio · T9.3 Mateo · T9.4 Damian |
| **HU-10** Indicadores | 3 | Nota ponderada 40/30/30, promedio, desviación e histograma; se recalculan solos al llegar notas | T10.1 Jostin · T10.2 Jostin · T10.3 Jostin · T10.4 Mateo |
| **HU-11** Riesgo y alertas | 5 | Crítico / En umbral / Aprobado con umbrales configurables y alertas **con evidencia** | T11.1 Jostin · T11.2 Jostin · T11.3 Damian · T11.4 Jostin |
| **HU-12** Desempeño por tema y ficha | 3 | Mapa de calor por tema y contenido, y ficha del estudiante con los temas ligados a cada nota | T12.1 Jostin · T12.2 Vinicio · T12.3 Mateo · T12.4 Damian |
| Gestión | — | Informe del sprint | TS2.1 Vinicio |

### Dónde está cada tarea en el código

| Tarea | Archivo(s) |
|---|---|
| T7.1 | `Academic/Moodle/MoodleClient.cs`, `MoodleModels.cs`, `Services/SyncService.cs` |
| T7.2 | `Academic/Services/GradeStateClassifier.cs` |
| T7.3 | `SyncService.cs` (bitácora `SincronizacionLog`, ahora también con avisos) |
| T7.4 | `Common/Messaging/*`, `Academic/Services/EventSnapshotBuilder.cs` |
| T7.5 | `frontend/src/pages/MateriaDetalle.jsx` |
| T7.6 | `tests/EduMonitor.Academic.Tests/SyncGradesTests.cs` |
| T8.1 | `Academic/Domain/Sprint2Entities.cs`, `Data/AcademicDbContext.cs` |
| T8.2 | `Academic/Services/EstructuraService.cs`, `Controllers/EstructuraController.cs` |
| T8.3 | `frontend/src/pages/EstructuraTematica.jsx` |
| T8.4 | `docs/sprint2/estructura_*.json` + endpoint `POST …/estructura/importar` |
| T9.1 | `ActividadContenido` (`Sprint2Entities.cs`), `Academic/Services/MapeoService.cs` |
| T9.2 | `Controllers/MapeoController.cs`, `MapeoEventDispatcher` (`EventSnapshotBuilder.cs`) |
| T9.3 | `frontend/src/pages/MapeoActividades.jsx` |
| T9.4 | `tests/…/EstructuraYMapeoTests.cs` |
| T10.1 | `Analytics/Services/CalculadoraIndicadores.cs`, `AnaliticaService.cs` |
| T10.2 | `tests/EduMonitor.Analytics.Tests/CalculadoraIndicadoresTests.cs` |
| T10.3 | `Analytics/Messaging/AnalyticsEventConsumer.cs`, `Services/SnapshotIngestor.cs` |
| T10.4 | `frontend/src/pages/DashboardCurso.jsx` |
| T11.1 | `CalculadoraIndicadores.NivelDe`, `UmbralMateria`, `PUT /api/umbrales` |
| T11.2 | `CalculadoraIndicadores.GenerarAlertas`, `AlertaRiesgo`, `Services/RecalculoService.cs` |
| T11.3 | Ventana «Umbrales de riesgo» y semáforo en `DashboardCurso.jsx` |
| T11.4 | `CalculadoraIndicadoresTests.cs`, `IngestaYConsultasTests.cs` |
| T12.1 | `CalculadoraIndicadores.DesempenoGrupo`, `AnaliticaService.ObtenerMapaCalorAsync` |
| T12.2 | `Analytics/Controllers/AnaliticaController.cs` |
| T12.3 | `frontend/src/pages/MapaCalor.jsx`, `FichaEstudiante.jsx` |
| T12.4 | `IngestaYConsultasTests.LaFicha_MuestraNotasConEstado_…` |

---

## 2. Qué cambia en la arquitectura

```
                    CalificacionesSincronizadas / MapeoActividadActualizado
 MS-B Académico ───────────────►  RabbitMQ (exchange edumonitor.events)  ───────────────► MS-C Analítica
 (Moodle, estructura, mapeo)         cola analitica.eventos  (+ cola de errores .dlq)      (indicadores, riesgo, mapa de calor)
```

* **RabbitMQ deja de ser decorativo.** MS-B publica los dos eventos de la sección 4.2 de la planificación; MS-C los consume, guarda su propia copia (base independiente) y recalcula. Si el bus cae, la sincronización **no falla**: termina con un aviso.
* **Migraciones de EF Core** reemplazan a `EnsureCreated()` en los tres servicios (acción de mejora 2 de la retrospectiva). Es un requisito: `EnsureCreated` no agrega tablas a una base existente.
* **MS-C ya no es un esqueleto**: tablas `Indicadores`, `AlertasRiesgo`, `Umbrales` y la copia de lectura.
* Los cálculos son **código determinista** (RN-09) en `CalculadoraIndicadores`, sin base de datos, con pruebas calculadas a mano.

### Reglas de cálculo (para defender)
| Concepto | Regla |
|---|---|
| Nota ponderada | `Σ(nota × peso) / Σ(peso)` solo de lo **ya evaluado** (calificada o cero). Se informa también el **% del peso evaluado** (RN-08) |
| Nota acumulada | Puntos ganados sobre 10 con el peso evaluado hasta ahora: `Σ(nota × peso) / 100` |
| Sin entrega / pendiente | **No** cuentan como cero: no se evalúan todavía. La nota cero explícita **sí** castiga |
| Nivel de riesgo | `< crítico` Crítico · `≥ aprobado` Aprobado · en medio, En umbral (defecto 5 y 7). Se compara la nota redondeada que ve el docente |
| Desviación estándar | Muestral (n − 1) |
| Alertas | **Nota baja** (< umbral crítico), **Descenso** (3 últimas notas evaluadas bajando, caída ≥ 1 punto) y **Sin entrega**. Cada una trae regla y evidencia |
| Desempeño por tema | Promedio ponderado por peso de las actividades **evaluadas** que miden ese tema; sin actividades evaluadas = «sin datos» |

---

## 3. Contenido del paquete

### Archivos modificados: 14 en total (⚠ revisar con `git diff` antes de aceptar)
| Archivo | Cambio |
|---|---|
| `src/BuildingBlocks/EduMonitor.Common/EduMonitor.Common.csproj` | +2 paquetes: `Microsoft.EntityFrameworkCore.Relational` y `RabbitMQ.Client` |
| `…/Academic.Api/Data/AcademicDbContext.cs` | +5 tablas (calificaciones, unidades, temas, contenidos, mapeo) |
| `…/Academic.Api/Moodle/MoodleClient.cs`, `MoodleModels.cs` | +2 llamadas a Moodle (notas y entregas) |
| `…/Academic.Api/Services/SyncService.cs` | Sincroniza notas y publica el evento. **Incluye** la validación de «0 cursos / 0 actividades» del Sprint 1 y la corrección de nombres vacíos |
| `…/Academic.Api/Program.cs` | Registro de servicios y migraciones |
| `…/Analytics.Api/EduMonitor.Analytics.Api.csproj` | +`Microsoft.EntityFrameworkCore.Design` (para migraciones) |
| `…/Analytics.Api/Data/AnalyticsDbContext.cs`, `Program.cs`, `Controllers/AnaliticaController.cs` | MS-C real |
| `…/Identity.Api/Data/DbSeeder.cs` | Migraciones en lugar de `EnsureCreated` |
| `src/Gateway/EduMonitor.Gateway/appsettings.json` | +2 rutas: `fichas-estudiante` y `umbrales-raiz` |
| `frontend/src/App.jsx` | Rutas nuevas (las pantallas «Próximamente» pasan a reales) |
| `frontend/src/pages/MateriaDetalle.jsx` | Botón sincronizar, notas con su estado y accesos directos |

> Si ya editaron alguno de estos archivos, **no lo sobrescriban a ciegas**: comparen con `git diff` y fusionen a mano.

Todo lo demás es **nuevo** (carpetas `Messaging/`, `Persistence/`, `Results/`, servicios, controladores, pruebas, pantallas, `docs/sprint2/`, `scripts/` y `postman/`).
No cambian: `docker-compose.yml`, `.env`, Dockerfiles ni el resto del Sprint 1.

---

## 4. Requisitos previos en Moodle (HU-07)

Como admin, en Moodle:

**a) Agregar una función al servicio EduMonitor**
*Administración del sitio → Servidor → Servicios web → Servicios externos → EduMonitor → Funciones → Agregar funciones*:
`mod_assign_get_submissions` (la función `gradereport_user_get_grade_items` ya estaba prevista desde el Sprint 1; compruebe que esté).

**b) Capacidades nuevas en el rol «Servicio web EduMonitor»**
*Usuarios → Permisos → Definir roles → Servicio web EduMonitor → Editar*, y **Permitir**:
`gradereport/user:view` · `mod/assign:viewgrades` · `moodle/grade:viewall` (ya estaba) · `moodle/grade:view`.

**c) Datos de demostración para los tres estados** (RN-08)
Con la nota que cargó el CSV del Sprint 1 ya tienen la *nota cero* (Joel, Trabajo autónomo 2) y *sin entrega* (celdas vacías). Falta **un caso «pendiente de calificar»**:
1. En una ventana privada, entra a Moodle como **`joel.herrera`** / `Estudiante#2026`.
2. Abre Programación Web → **APE 2** → **Agregar entrega** → escribe un texto → **Guardar cambios**.
3. Esa actividad queda **entregada y sin nota**, que es justo «pendiente de calificar».

**d) Probar las dos funciones nuevas** (reemplaza `TU_TOKEN`; `courseid` = id del curso en Moodle, normalmente 2 y 3):
```
http://localhost:8081/webservice/rest/server.php?wstoken=TU_TOKEN&wsfunction=gradereport_user_get_grade_items&courseid=2&userid=0&moodlewsrestformat=json
http://localhost:8081/webservice/rest/server.php?wstoken=TU_TOKEN&wsfunction=mod_assign_get_submissions&assignmentids[0]=1&moodlewsrestformat=json
```
La primera debe devolver `usergrades` con `gradeitems`; la segunda, `assignments` con `submissions`. Si alguna da `accessexception`, falta la función o una capacidad. **No compartan estas URLs: llevan el token.**

> Si la segunda función no está disponible, el sistema **no se rompe**: marca las notas vacías como «sin entrega» y lo avisa al sincronizar.

---

## 5. Integración con Git, paso a paso

Convención: `main` = estable (lo que se demuestra) · `develop` = integración · `feature/*` = trabajo. Todo entra por **Pull Request revisado por otro integrante** (Definición de Terminado).

### 5.1 Preparar
```powershell
cd C:\dev\edumonitor-ai-sprint1\edumonitor-ai
git status                      # debe decir "nothing to commit, working tree clean"
git checkout main
git pull
```
Si el Sprint 1 aún no tiene etiqueta ni rama `develop`, créalas una sola vez:
```powershell
git tag -a sprint-1 -m "Cierre del Sprint 1"
git push origin sprint-1
git checkout -b develop
git push -u origin develop
```
Si `develop` ya existe: `git checkout develop` y `git pull`.

### 5.2 Rama del Sprint 2
```powershell
git checkout -b feature/sprint-2
```

### 5.3 Copiar el paquete
Descomprime el zip **en la raíz del repositorio** (junto a `docker-compose.yml`), aceptando reemplazar los archivos modificados:
```powershell
Expand-Archive -Path "$HOME\Downloads\edumonitor-ai-sprint2.zip" -DestinationPath . -Force
git status
git diff --stat                 # deben aparecer exactamente los 14 archivos modificados de la sección 3
```
Revisa los cambios de lo modificado (especialmente `SyncService.cs` y los `Program.cs`):
```powershell
git diff src/Services/Academic/EduMonitor.Academic.Api/Services/SyncService.cs
```

### 5.4 Agregar el proyecto de pruebas nuevo a la solución
```powershell
dotnet sln EduMonitor.sln add tests/EduMonitor.Analytics.Tests/EduMonitor.Analytics.Tests.csproj
dotnet build EduMonitor.sln
dotnet test EduMonitor.sln
```
Esperado: **compilación correcta** y **90 pruebas aprobadas** (Identity 9 · Academic 44 · Analytics 37).
Si algo falla, **no sigan**: copien el mensaje completo y lo corregimos.

### 5.5 Commits (uno por grupo lógico)
Los commits intermedios **compilan**, pero los servicios solo arrancan completos tras el commit 5 (integración).

```powershell
# 1) Base común: migraciones, mensajería y resultados
git add src/BuildingBlocks/EduMonitor.Common/EduMonitor.Common.csproj src/BuildingBlocks/EduMonitor.Common/Messaging src/BuildingBlocks/EduMonitor.Common/Persistence src/BuildingBlocks/EduMonitor.Common/Results
git add src/Services/Identity/EduMonitor.Identity.Api/Data
git add src/Services/Academic/EduMonitor.Academic.Api/Data/AcademicDbContextFactory.cs
git add src/Services/Analytics/EduMonitor.Analytics.Api/Data/AnalyticsDbContextFactory.cs src/Services/Analytics/EduMonitor.Analytics.Api/EduMonitor.Analytics.Api.csproj
git add scripts/reset-databases.ps1
git commit -m "chore(db): migraciones de EF Core, mensajería RabbitMQ y resultados comunes"

# 2) HU-07: calificaciones
git add src/Services/Academic/EduMonitor.Academic.Api/Domain/Sprint2Entities.cs src/Services/Academic/EduMonitor.Academic.Api/Data/AcademicDbContext.cs
git add src/Services/Academic/EduMonitor.Academic.Api/Moodle
git add src/Services/Academic/EduMonitor.Academic.Api/Services/GradeStateClassifier.cs src/Services/Academic/EduMonitor.Academic.Api/Services/EventSnapshotBuilder.cs src/Services/Academic/EduMonitor.Academic.Api/Services/SyncService.cs
git add src/Services/Academic/EduMonitor.Academic.Api/Contracts/Sprint2Dtos.cs
git add src/Services/Academic/EduMonitor.Academic.Api/Controllers/MateriaControllerBase.cs src/Services/Academic/EduMonitor.Academic.Api/Controllers/CalificacionesController.cs
git add tests/EduMonitor.Academic.Tests/TestSupport.cs tests/EduMonitor.Academic.Tests/GradeStateClassifierTests.cs tests/EduMonitor.Academic.Tests/SyncGradesTests.cs
git commit -m "feat(HU-07): sincronizar calificaciones distinguiendo cero, sin entrega y pendiente"

# 3) HU-08 y HU-09: estructura y mapeo
git add src/Services/Academic/EduMonitor.Academic.Api/Services/EstructuraService.cs src/Services/Academic/EduMonitor.Academic.Api/Services/MapeoService.cs
git add src/Services/Academic/EduMonitor.Academic.Api/Controllers/EstructuraController.cs src/Services/Academic/EduMonitor.Academic.Api/Controllers/MapeoController.cs
git add tests/EduMonitor.Academic.Tests/EstructuraYMapeoTests.cs
git commit -m "feat(HU-08,HU-09): estructura temática y mapeo actividad-contenido"

# 4) HU-10, HU-11 y HU-12: análisis en MS-C
git add src/Services/Analytics/EduMonitor.Analytics.Api/Domain src/Services/Analytics/EduMonitor.Analytics.Api/Data/AnalyticsDbContext.cs
git add src/Services/Analytics/EduMonitor.Analytics.Api/Contracts src/Services/Analytics/EduMonitor.Analytics.Api/Services src/Services/Analytics/EduMonitor.Analytics.Api/Messaging
git add tests/EduMonitor.Analytics.Tests EduMonitor.sln
git commit -m "feat(HU-10,HU-11,HU-12): indicadores, riesgo con alertas y desempeño por tema"

# 5) Integración
git add src/Services/Academic/EduMonitor.Academic.Api/Program.cs src/Services/Analytics/EduMonitor.Analytics.Api/Program.cs src/Services/Analytics/EduMonitor.Analytics.Api/Controllers/AnaliticaController.cs
git add src/Gateway/EduMonitor.Gateway/appsettings.json
git commit -m "feat(sprint2): integrar servicios de MS-B y MS-C y rutas del Gateway"
```
Los commits 6 a 8 se hacen en los pasos 5.7 y 5.8 (migraciones, frontend y documentación).

### 5.6 Generar las migraciones (una sola vez)
```powershell
dotnet tool install --global dotnet-ef --version 8.0.8        # si ya lo tienes: dotnet tool update --global dotnet-ef --version 8.0.8
dotnet ef migrations add Inicial --project src/Services/Identity/EduMonitor.Identity.Api
dotnet ef migrations add Inicial --project src/Services/Academic/EduMonitor.Academic.Api
dotnet ef migrations add Inicial --project src/Services/Analytics/EduMonitor.Analytics.Api
```
Cada comando crea una carpeta `Migrations/`. No requieren que SQL Server esté encendido.

```powershell
# 6) Migraciones
git add src/Services/Identity/EduMonitor.Identity.Api/Migrations src/Services/Academic/EduMonitor.Academic.Api/Migrations src/Services/Analytics/EduMonitor.Analytics.Api/Migrations
git commit -m "feat(db): migración inicial de los tres microservicios"
```

### 5.7 Frontend y documentación
```powershell
# 7) Frontend (M4 a M8)
git add frontend/src
git commit -m "feat(frontend): estructura, mapeo, dashboard, mapa de calor y ficha del estudiante"

# 8) Documentación, Postman y datos de estructura
git add docs postman
git commit -m "docs(sprint2): guía, colección de Postman y estructura de ejemplo"
```
Antes de seguir confirma que `git status` no muestra `.env` ni `node_modules`.

### 5.8 Subir y Pull Request
```powershell
git push -u origin feature/sprint-2
```
En GitHub: **Compare & pull request** → base `develop` ← compare `feature/sprint-2` → llenar la plantilla (historia, cómo se probó, DoD). Pide la revisión de **otro integrante** y espera que el pipeline (GitHub Actions) quede en verde. Con la aprobación: **Merge pull request**.
```powershell
git checkout develop
git pull
git branch -d feature/sprint-2
```
Al cerrar el sprint: PR de `develop` a `main` y `git tag -a sprint-2 -m "Cierre del Sprint 2"`.

### 5.9 Si trabajan en paralelo (una rama por integrante)
| Rama | Quién | Qué sube |
|---|---|---|
| `feature/HU-07-calificaciones` | Damian | commit 1 y 2 |
| `feature/HU-08-09-estructura-mapeo` | Vinicio | commit 3 (sobre HU-07 ya integrada) |
| `feature/HU-10-12-analitica` | Jostin | commit 4 |
| `feature/frontend-sprint2` | Mateo | commit 7 |
Integren en ese orden y dejen el commit 5 (integración) para el final. Si dos personas tocan `Program.cs`, hagan *rebase* sobre `develop` antes de abrir el PR.

### 5.10 Conflictos
Si Git marca conflicto en un archivo: ábrelo, busca `<<<<<<<`, deja la versión correcta, `git add <archivo>` y `git commit`. En `SyncService.cs` y en los `Program.cs` **gana la versión del Sprint 2**, conservando cualquier cambio propio que ya tuvieran.

---

## 6. Puesta en marcha

### 6.1 Reiniciar las bases (una sola vez)
Las bases del Sprint 1 se crearon con `EnsureCreated` y no se pueden migrar. Se recrean (Moodle no se toca; los datos se vuelven a traer al sincronizar):
```powershell
.\scripts\reset-databases.ps1
```
Si PowerShell bloquea el script: `Set-ExecutionPolicy -Scope Process Bypass` y vuelve a ejecutarlo.

### 6.2 Levantar
```powershell
docker compose up -d --build
docker compose ps
docker compose logs academic-api --tail 15
docker compose logs analytics-api --tail 15
```
Debes ver en los logs:
* `Base de datos de AcademicDbContext actualizada` (y de `AnalyticsDbContext`, `IdentityDbContext`)
* `Usuarios de prueba sembrados`
* `Consumidor conectado a la cola analitica.eventos`

En http://localhost:15672 → **Queues**: aparece `analitica.eventos` (y `analitica.eventos.dlq`, que debe estar en **0**).

### 6.3 Flujo de prueba completo (demo)
1. Entra a http://localhost:5174 como `docente1@uta.edu.ec`.
2. **Mis materias → Sincronizar con Moodle**.
3. **Estructura temática** → «Importar desde JSON» → pega `docs/sprint2/estructura_programacion_web.json` (la materia **Programación Web**) → Importar. Repite con `estructura_base_de_datos.json` para la otra materia.
4. **Mapear actividades** → asigna contenidos a cada actividad (ej.: Evaluación 1 → Etiquetas semánticas + Selectores CSS).
5. **Dashboard**: indicadores, histograma y estudiantes por riesgo. Prueba **Umbrales de riesgo** (cambia 5 → 4 y mira cómo se reclasifican).
6. **Mapa de calor por tema** y, desde el nombre de un estudiante, su **ficha**.
7. En Postman importa `postman/EduMonitor.Sprint2.postman_collection.json`, ajusta `materia_id` (el id de Programación Web en EduMonitor) y ejecuta el **Runner**.

---

## 7. Verificación por historia (criterios de aceptación)

**HU-07**
1. Notas del sistema = notas de Moodle → comparar la tabla «Calificaciones sincronizadas» (`Ver materia`) con el libro de Moodle.
2. Nota cero ≠ sin entrega ≠ pendiente → celdas de colores distintos (Joel: Trabajo autónomo 2 = cero, APE 2 = pendiente, Trabajo autónomo 3 = sin entrega).
3. Bitácora → en SSMS: `SELECT TOP 5 Fecha, Tipo, Exitosa, Mensaje FROM SincronizacionLog ORDER BY Fecha DESC` (DB_Academico).
4. Evento publicado y Moodle intacto → `docker compose logs academic-api | findstr Evento`; Moodle no se modificó (RN-06).

**HU-08** → crear, editar y eliminar; eliminar un contenido asignado pide confirmación; un docente ajeno recibe 403.
**HU-09** → asignar uno o varios contenidos; actividades sin mapear salen «pendientes»; sincronizar de nuevo **conserva** el mapeo.
**HU-10** → nota ponderada igual al cálculo manual; muestra escala 0–10 y % del peso evaluado; el histograma separa los dos grupos; al sincronizar, el dashboard se actualiza solo.
**HU-11** → con 5 y 7, una nota de 4,6 es **Crítico**; al cambiar el umbral se reclasifica; cada alerta muestra nota, actividad y regla; el aviso indica que es orientativa.
**HU-12** → el mapa usa semáforo; en la ficha, una nota baja muestra los temas/contenidos que evalúa; contenidos sin actividades salen «—» (sin datos).

---

## 8. Si algo falla

| Síntoma | Causa probable | Solución |
|---|---|---|
| El servicio se cae con «no tiene migraciones» | Falta el paso 5.6 | Generar las migraciones y reconstruir (`docker compose up -d --build`) |
| `Invalid object name 'Calificaciones'` | La base del Sprint 1 sigue ahí | Ejecutar `.\scripts\reset-databases.ps1` |
| Sincronizar da «no se pudieron obtener las calificaciones» + `accessexception` | Falta `gradereport_user_get_grade_items` o `gradereport/user:view` | Sección 4 (a y b) |
| Nada aparece como «pendiente de calificar» | Falta la entrega de prueba o `mod_assign_get_submissions` | Sección 4 (a, b y c). Mientras tanto se muestra «sin entrega» y el sistema lo avisa |
| El dashboard dice «Aún no hay datos» | El evento no llegó a MS-C | `docker compose logs analytics-api --tail 30`; revisar RabbitMQ. Sincronizar de nuevo |
| Mensajes en `analitica.eventos.dlq` | Un evento no se pudo procesar | Ver el error en `docker compose logs analytics-api`; corregir y volver a sincronizar |
| «el aviso al análisis no se pudo enviar» | RabbitMQ apagado | `docker compose up -d rabbitmq`; sincronizar otra vez |
| `dotnet ef` no encuentra el proyecto | Comando fuera de la raíz | Ejecutar desde la carpeta que contiene `EduMonitor.sln` |
| Pruebas con SQLite fallan | Paquete sin restaurar | `dotnet restore EduMonitor.sln` |

---

## 9. Decisiones y límites conocidos (transparencia)

**Deuda técnica resuelta en este sprint**
* `EnsureCreated()` → migraciones de EF Core (acción 2 de la retrospectiva).
* Nombres de estudiantes vacíos en la pantalla M3 (caía en `SyncService`).
* Un resultado vacío de Moodle ya no se muestra como éxito (ya estaba en el Sprint 1; se conserva).
* Eventos con cola de errores (dead letter), consumidor idempotente y descarte de eventos antiguos.
* Control de acceso por curso en **todos** los endpoints nuevos (RN-07) y validación de que cada contenido pertenece a la materia (sin accesos cruzados).

**Límites que conviene conocer**
* **«Descenso»** usa el orden de creación de las actividades en Moodle (`cmid`) como orden cronológico: Moodle no entrega la fecha de calificación en estas llamadas.
* **Nota ponderada** = promedio de lo ya evaluado. Difiere del total de Moodle cuando hay actividades sin nota; por eso siempre se muestra el % de peso evaluado.
* La guía del Sprint 1 decía que Lucía tiene 8,92; el sistema redondea «medio hacia arriba» y muestra **8,93** (892,5 / 100).
* El análisis recibe nombres y apellidos de los estudiantes (los necesita el docente en pantalla); **no** recibe correos. El modelo de lenguaje (Sprint 3) recibirá solo identificadores anónimos (RN-11).
* Si RabbitMQ está caído, publicar un evento puede tardar hasta 3 s antes de continuar sin él.
* Las pruebas con base de datos (InMemory y SQLite) se ejecutan en su equipo; si una falla por un detalle del proveedor, avísennos.

---

## 10. Actualizar el informe del Sprint 2 (con el formato del Sprint 1)
* Secciones 6.2 y 6.3: agregar los endpoints y tablas de este sprint (ver sección 2).
* Pruebas de aceptación: los casos de la sección 7 con sus capturas.
* 7.1: Identity 9 · Academic 44 · Analytics 37 y la ejecución de `postman/EduMonitor.Sprint2.postman_collection.json`.
* Impedimentos reales del sprint (migraciones sobre base existente, permisos de Moodle para notas y entregas).
