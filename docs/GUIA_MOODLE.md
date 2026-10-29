# Guía del Moodle de prueba (HU-01 y HU-02)

Responsables: **Damian Bastidas** (T1.1, T1.2, T2.1) y **Jostin Sigcha** (T1.3, T1.4, T2.2).
Todos los datos son **ficticios** (RN-11). Los archivos CSV están en la carpeta `moodle/`.

---

## T1.1 · Levantar Moodle + MariaDB

```bash
cp .env.example .env          # completar contraseñas
docker compose up -d mariadb moodle
docker compose logs -f moodle # la PRIMERA vez tarda 5-10 min (instala la BD)
```

Abrir **http://localhost:8081** e ingresar con `MOODLE_ADMIN_USER` / `MOODLE_ADMIN_PASSWORD` del `.env`.

> **Imagen alternativa.** Bitnami trasladó sus imágenes gratuitas al repositorio `bitnamilegacy/*`.
> Si `docker compose pull` falla, revisar en Docker Hub la etiqueta disponible de `bitnamilegacy/moodle`
> o usar otra imagen de Moodle (por ejemplo, la oficial `moodlehq/moodle-php-apache` + código de Moodle).
> Plan de contingencia del Sprint 0 (E0.6): instancia de laboratorio con los mismos datos ficticios.

Evidencia para el informe: captura de la página principal de Moodle y de `docker ps`.

---

## T1.2 · Crear las 2 materias y la ponderación 40 / 30 / 30

### a) Crear los cursos (carga masiva)
*Administración del sitio → Cursos → Subir cursos* → subir `moodle/1_cursos.csv`.

| shortname | fullname |
|---|---|
| PW-401-B | Programación Web - Cuarto B |
| BD-402-B | Base de Datos - Cuarto B |

### b) Categorías de calificación (en **cada** curso)
*Curso → Calificaciones → Configuración del libro de calificaciones* (o «Configuración de calificaciones»).

1. Editar la categoría raíz del curso → **Cálculo total: «Media ponderada de calificaciones»**.
2. **Añadir categoría** tres veces:

| Categoría | Peso (en la categoría raíz) | Equivalente UTA |
|---|---|---|
| Evaluaciones | 40 | ACD (contacto con el docente) |
| APE | 30 | Aprendizaje práctico-experimental |
| Trabajo autónomo | 30 | AA (aprendizaje autónomo) |

3. Dentro de cada categoría usar «Media de calificaciones» (todas las actividades valen igual).
4. Escala de todas las actividades: **0 a 10**, nota para aprobar **7** (RN-01).

Evidencia: captura del libro de calificaciones mostrando los pesos 40 / 30 / 30.

---

## T1.3 · Actividades del 1.er parcial (RN-03)

En cada curso crear una sección «Primer Parcial» y, dentro, **7 actividades tipo Tarea** (calificación máxima 10).
Usar **exactamente estos nombres**: MS-B clasifica la actividad por su nombre (Evaluación / APE / Trabajo autónomo).

| Actividad | Categoría de calificación | Peso resultante |
|---|---|---|
| Evaluación 1 | Evaluaciones | 40 % |
| APE 1 | APE | 15 % |
| APE 2 | APE | 15 % |
| Trabajo autónomo 1 | Trabajo autónomo | 7,5 % |
| Trabajo autónomo 2 | Trabajo autónomo | 7,5 % |
| Trabajo autónomo 3 | Trabajo autónomo | 7,5 % |
| Trabajo autónomo 4 | Trabajo autónomo | 7,5 % |

Al crear cada tarea: *Calificación → Categoría de calificación* = la indicada en la tabla.

---

## T1.4 · Usuarios, matrículas y notas con casos de riesgo

### a) Usuarios y matrícula
*Administración del sitio → Usuarios → Subir usuarios* → `moodle/2_usuarios.csv`
(«Tipo de subida: Agregar nuevos y actualizar existentes»). Crea:

- `docente1@uta.edu.ec` → profesor (editingteacher) de las 2 materias.
- `docente2@uta.edu.ec` → sin materias (se usa para la **prueba de permisos T6.5**).
- 10 estudiantes ficticios matriculados en ambas materias.

> El correo del docente en Moodle **debe ser el mismo** que su usuario en EduMonitor (MS-A): así MS-B encuentra sus cursos.

### b) Calificaciones
*Curso → Calificaciones → Importar → Archivo CSV* → `moodle/3_notas_PW-401-B.csv` (y `4_notas_BD-402-B.csv` en Base de Datos).
Mapear «Identificar usuario por: **email**» y cada columna con su actividad.
Las celdas vacías representan actividades **sin entrega / pendientes** (RN-08; se distinguen en el Sprint 2).

Distribución esperada en Programación Web (nota ponderada 40/30/30):

| Estudiante | Nota | Nivel (RN-04) |
|---|---|---|
| Lucía Ramírez | 8,92 | Aprobado |
| Sofía Villacís | 8,16 | Aprobado |
| Carlos Mena | 7,81 | Aprobado |
| Bryan Gavilanes | 7,46 | Aprobado |
| Andrés Torres | 6,39 | En umbral |
| Valeria Sánchez | 5,89 | En umbral |
| Diego Paredes | 4,80 | Crítico (descenso en las últimas notas) |
| Camila Núñez | 3,55 | Crítico (2 trabajos sin entrega) |
| Daniela Proaño | 3,23 | Crítico |
| Joel Herrera | 2,27 | Crítico (nota cero, sin entrega y pendiente) |

El curso queda con dos grupos claramente separados: justo el caso «el promedio esconde la realidad» del enunciado.

---

## T2.1 · Servicios web REST y token de alcance limitado

1. *Administración del sitio → Funcionalidades avanzadas* → **Habilitar servicios web** ✔.
2. *Administración del sitio → Servidor → Servicios web → Gestionar protocolos* → habilitar **REST**.
3. Crear el usuario técnico `ws_edumonitor` (correo `ws@edumonitor.local`). No es la cuenta personal del docente (HU-02).
4. Crear un rol de sistema **«Servicio web EduMonitor»** con estas capacidades (mínimo privilegio):
   - `webservice/rest:use`
   - `moodle/course:view`, `moodle/course:viewparticipants`, `moodle/course:viewhiddencourses`
   - `moodle/user:viewdetails`, `moodle/user:viewalldetails`, `moodle/site:viewuseridentity`
   - `moodle/site:accessallgroups`
   - `moodle/grade:viewall` *(se usará en el Sprint 2)*
   y asignarlo a `ws_edumonitor` en *Usuarios → Permisos → Asignar roles de sistema*.
   *(Solo si algo falla en desarrollo, asignar temporalmente el rol «Gestor» y documentarlo como deuda técnica).*
5. *Servicios web → Servicios externos → Agregar*: nombre **EduMonitor**, ✔ Habilitado, ✔ **Solo usuarios autorizados**.
6. En «Funciones» del servicio agregar:
   - `core_webservice_get_site_info`
   - `core_user_get_users_by_field`
   - `core_enrol_get_users_courses`
   - `core_enrol_get_enrolled_users`
   - `core_course_get_contents`
   - `mod_assign_get_assignments`
   - `gradereport_user_get_grade_items` *(Sprint 2)*
7. «Usuarios autorizados» → agregar `ws_edumonitor`.
8. *Servicios web → Gestionar tokens → Crear token*: usuario `ws_edumonitor`, servicio **EduMonitor**.
9. Copiar el token en el `.env` → `MOODLE_TOKEN=...` y reiniciar: `docker compose up -d academic-api`.
10. Para que los correos de los estudiantes lleguen por la API: *Administración del sitio → Usuarios → Permisos → Políticas de usuario →* `showuseridentity` = **email**.

> ⚠️ El token **nunca** se sube al repositorio (HU-02, criterio 3). Solo vive en `.env`.

Verificar los nombres de funciones en *Administración del sitio → Servidor → Servicios web → Documentación de la API*.

---

## T2.2 · Probar las funciones en Postman

Importar `postman/EduMonitor.postman_collection.json`, configurar las variables `moodle_url` (`http://localhost:8081`) y `moodle_token`, y ejecutar la carpeta **«Moodle directo»**:

| Prueba | Resultado esperado |
|---|---|
| `core_webservice_get_site_info` | Devuelve `sitename` y `release` (criterio 1 de HU-02) |
| `core_user_get_users_by_field` (docente1) | Devuelve el usuario con su `id` |
| `core_enrol_get_users_courses` | Devuelve PW-401-B y BD-402-B |
| `core_enrol_get_enrolled_users` | 11 usuarios (10 estudiantes + docente) |
| `core_course_get_contents` | Secciones con las 7 tareas |
| `core_user_create_users` (función NO autorizada) | `accessexception` → **Moodle rechaza** (criterio 2 de HU-02) |

Guardar capturas de cada respuesta como evidencia del sprint.
