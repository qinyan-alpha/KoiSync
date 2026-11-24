# -*- coding: utf-8 -*-
"""Load SummerEcho v036 and capture one native, unscripted contact cycle in Studio.

Normal-plugin integration probe through IronPython/Unity Console.  It never saves the scene and
does not load a funscript or open a device transport.  The ordinary scene camera and
a fixed diagnostic side camera are rendered after 24 settled updates at every sample.
"""
import System
import json
import traceback
import coroutine
import Studio
import UnityEngine as U
from System.IO import Directory, File, FileInfo
from System.Reflection import BindingFlags


CARD = 'E:/Galgames/kk/UserData/Studio/scene/qinnyann/SummerEcho_120s_Light/SummerEcho_120s_BPVisualFix_v036.png'
OUT = 'E:/project/c#/KoiSync/artifacts/scene-preview/v036-native-contact-20261024-v0137-run1/'
REPORT = OUT + 'runtime.json'
PROGRESS = OUT + 'progress.json'
TIMES = [81.000, 81.033, 81.067, 81.100, 81.133, 81.200, 81.267]
F = (BindingFlags.Public | BindingFlags.NonPublic |
     BindingFlags.Instance | BindingFlags.Static)


def typ(name):
    return next(a.GetType(name) for a in System.AppDomain.CurrentDomain.GetAssemblies()
                if a.GetType(name) is not None)


def member(obj, name):
    if obj is None:
        return None
    item_type = obj if isinstance(obj, System.Type) else obj.GetType()
    target = None if isinstance(obj, System.Type) else obj
    while item_type is not None:
        field = item_type.GetField(name, F | BindingFlags.DeclaredOnly)
        if field is not None:
            return field.GetValue(target)
        prop = item_type.GetProperty(name, F | BindingFlags.DeclaredOnly)
        if prop is not None and prop.GetIndexParameters().Length == 0:
            try:
                return prop.GetValue(target, None)
            except:
                pass
        item_type = item_type.BaseType
    return None


def set_member(obj, name, value):
    item_type = obj.GetType()
    while item_type is not None:
        field = item_type.GetField(name, F | BindingFlags.DeclaredOnly)
        if field is not None:
            field.SetValue(obj, value)
            return
        prop = item_type.GetProperty(name, F | BindingFlags.DeclaredOnly)
        if prop is not None and prop.CanWrite:
            prop.SetValue(obj, value, None)
            return
        item_type = item_type.BaseType
    raise Exception('Member not found: ' + name)


def invoke(obj, name, args=None):
    item_type = obj if isinstance(obj, System.Type) else obj.GetType()
    target = None if isinstance(obj, System.Type) else obj
    method = item_type.GetMethod(name, F)
    if method is None:
        raise Exception('Method not found: ' + item_type.FullName + '.' + name)
    values = None if args is None else System.Array[System.Object](args)
    return method.Invoke(target, values)


def v3(value):
    return U.Vector3(float(value.X), float(value.Y), float(value.Z))


def xyz(value):
    return [float(value.x), float(value.y), float(value.z)]


def xyzw(value):
    return [float(value.x), float(value.y), float(value.z), float(value.w)]


def vec_add(a, b):
    return U.Vector3(a.x + b.x, a.y + b.y, a.z + b.z)


def vec_sub(a, b):
    return U.Vector3(a.x - b.x, a.y - b.y, a.z - b.z)


def vec_scale(value, scale):
    return U.Vector3(value.x * scale, value.y * scale, value.z * scale)


def motion(value):
    motion_type = typ('KoiSync.Core.MotionVector')
    return System.Activator.CreateInstance(
        motion_type, System.Array[System.Object]([
            System.Double(value.x), System.Double(value.y), System.Double(value.z)]))


def motion_delta(a, b):
    motion_type = typ('KoiSync.Core.MotionVector')
    return System.Activator.CreateInstance(
        motion_type, System.Array[System.Object]([
            System.Double(a.x - b.x), System.Double(a.y - b.y),
            System.Double(a.z - b.z)]))


def alignment(origin, right, up):
    return typ('KoiSync.Core.AlignmentFrame').GetMethod('Build', F).Invoke(
        None, System.Array[System.Object]([origin, right, up]))


def transform_map(root):
    result = {}
    for node in root.GetComponentsInChildren[U.Transform](True):
        if node.name not in result:
            result[node.name] = node
    return result


def first(mapping, names):
    for name in names:
        if name in mapping:
            return mapping[name]
    return None


def save(path, payload):
    File.WriteAllText(path, json.dumps(payload, ensure_ascii=True, indent=2))


def capture(camera, path, width, height, position=None, rotation=None, fov=None):
    old_position = camera.transform.position
    old_rotation = camera.transform.rotation
    old_fov = camera.fieldOfView
    old_aspect = camera.aspect
    old_target = camera.targetTexture
    old_active = U.RenderTexture.active
    render = U.RenderTexture(width, height, 24)
    texture = U.Texture2D(width, height, U.TextureFormat.RGB24, False)
    try:
        if position is not None:
            camera.transform.position = position
        if rotation is not None:
            camera.transform.rotation = rotation
        if fov is not None:
            camera.fieldOfView = fov
        camera.aspect = float(width) / float(height)
        camera.targetTexture = render
        camera.Render()
        U.RenderTexture.active = render
        texture.ReadPixels(U.Rect(0, 0, width, height), 0, 0)
        texture.Apply()
        File.WriteAllBytes(path, texture.EncodeToPNG())
    finally:
        camera.targetTexture = old_target
        U.RenderTexture.active = old_active
        camera.transform.position = old_position
        camera.transform.rotation = old_rotation
        camera.fieldOfView = old_fov
        camera.aspect = old_aspect
        U.Object.Destroy(texture)
        U.Object.Destroy(render)


def run():
    U.Application.runInBackground = True
    Directory.CreateDirectory(OUT)
    studio = Studio.Studio.Instance
    report = {
        'status': 'running',
        'card': CARD,
        'drive': 'normal plugin discovery/profile/pipeline; no script or hardware',
        'times': TIMES,
        'settle_frames': 24,
        'camera_policy': {
            'context': 'no character render; diagnostic geometry only',
            'contact': 'isolated camera in game world coordinates, sleeve/rod/entry only',
        },
        'frames': [],
    }
    save(REPORT, report)

    class Work(System.Collections.IEnumerator):
        def __init__(self):
            self.current = None
            self.phase = 'startup_wait'
            self.started = float(U.Time.realtimeSinceStartup)
            self.load_started = self.started
            self.index = 0
            self.wait = 0
            self.refresh_attempted = False
            self.timeline_type = None
            self.timeline = None
            self.host = None
            self.preview = None
            self.nodes = None
            self.male = None
            self.female = None
            self.male_bones = None
            self.female_bones = None
            self.bp_points = None
            self.bp_base_length = 0.0
            self.side_position = None
            self.side_rotation = None
            self.side_focus = None
            self.frame_camera = None
            self.frame_sample = None
            self.frame_drive = None
            self.frame_sleeve = None
            self.frame_rod = None
            self.frame_context_path = None
            self.frame_contact_path = None
            self.frame_record = None
            self.old_audio_pause = bool(U.AudioListener.pause)

        def get_Current(self):
            return self.current

        Current = property(get_Current)

        def Reset(self):
            raise System.NotSupportedException()

        def progress(self, **extra):
            payload = {
                'status': 'running',
                'phase': self.phase,
                'index': self.index,
                'time': TIMES[self.index] if self.index < len(TIMES) else None,
                'elapsed_realtime_seconds': float(U.Time.realtimeSinceStartup) - self.started,
            }
            payload.update(extra)
            save(PROGRESS, payload)

        def seek(self, value):
            self.timeline_type.GetMethod('Pause', F).Invoke(None, None)
            self.timeline_type.GetMethod('Seek', F).Invoke(
                None, System.Array[System.Object]([System.Single(value)]))
            for before in (True, False):
                self.timeline_type.GetMethod('Interpolate', F).Invoke(
                    self.timeline, System.Array[System.Object]([System.Boolean(before)]))
            if self.nodes is not None:
                self.nodes.GetType().GetMethod('ApplyNodesConstraints', F).Invoke(self.nodes, None)

        def configure_binding(self):
            male_control = studio.dicObjectCtrl[539]
            female_control = studio.dicObjectCtrl[403]
            self.male = male_control.charInfo
            self.female = female_control.charInfo
            if self.male is None or self.female is None:
                raise Exception('Expected v036 character controls 539/403 are missing')
            self.male_bones = transform_map(self.male.transform)
            self.female_bones = transform_map(self.female.transform)
            entry = first(self.female_bones, ['cf_J_Vagina_root', 'cf_j_vagina_root'])
            inner = first(self.female_bones, ['cf_j_spine02', 'cf_j_spine01', 'cf_j_hips'])
            rig = first(self.male_bones, ['cf_j_root', 'cm_J_root', 'cm_j_root'])
            if entry is None or inner is None or rig is None:
                raise Exception('The expected entry, internal direction, or male rig root is missing')

            controller = None
            for component in self.male.transform.GetComponents[U.Component]():
                if (component is not None and component.GetType().FullName ==
                        'Core_BetterPenetration.BetterPenetrationController'):
                    controller = component
                    break
            if controller is None:
                raise Exception('Male BetterPenetration controller is missing')
            agent = member(controller, 'danAgent')
            points = member(member(agent, 'm_danPoints'), 'danPoints')
            if points is None:
                raise Exception('BetterPenetration point chain is missing')
            self.bp_points = [member(point, 'transform') for point in points]
            if len(self.bp_points) < 2 or any(point is None for point in self.bp_points):
                raise Exception('BetterPenetration point chain is incomplete')
            self.bp_base_length = float(member(agent, 'm_baseDanLength'))
            if self.bp_base_length <= 0.0:
                raise Exception('BetterPenetration base length is invalid')
            report['selected_source'] = {
                'mode': 'normal plugin Selected; explicit object IDs used only to inspect BP diagnostics',
                'male_object_id': 539,
                'female_object_id': 403,
                'male_name': unicode(self.male.fileParam.fullname),
                'female_name': unicode(self.female.fileParam.fullname),
                'profile': 'VAGINAL',
                'bp': True,
                'point_count': len(self.bp_points),
                'bp_base_length_m': self.bp_base_length,
                'entry_bone': unicode(entry.name),
                'internal_direction_bone': unicode(inner.name),
                'male_reference_bone': unicode(rig.name),
            }

        def configure(self):
            view = member(self.host, 'ViewSettings')
            view.ShowWireframe = True
            view.ShowContactModel = True
            view.XRay = True
            view.Axes = False
            view.Plane = False
            view.Bounds = False
            view.Arcs = False
            view.Labels = False
            view.ContactModelScale = 1.0
            view.FixedRodRadius = 0.012
            view.FollowVisibleRodLength = False
            set_member(self.host, 'DebugVisible', True)
            panel = member(self.host, 'panel')
            if panel is not None:
                invoke(panel, 'SetVisible', [System.Boolean(False)])
            self.preview = member(self.host, 'contactPreview')
            report['preview_settings'] = {
                'zipmod_sleeve_scale': float(view.ContactModelScale),
                'fixed_rod_radius_m': float(view.FixedRodRadius),
                'follow_visible_bp_length': bool(view.FollowVisibleRodLength),
                'xray': bool(view.XRay),
                'other_wireframe_helpers': False,
            }

        def candidate(self):
            selected = member(self.host, 'Selected')
            if selected is None or member(selected, 'Native') is None:
                raise Exception('Normal plugin sampling did not select a source: ' + unicode(member(member(self.host, 'StudioAdapter'), 'Discovery')))
            sample = member(selected, 'Native')
            if not sample.HasShaftPose:
                raise Exception('Selected normal source has no shaft pose')
            return selected

        def set_side_camera(self, sample):
            axis = v3(sample.SourceRootDirection).normalized
            side = v3(sample.TargetReferenceFrame.X)
            side = vec_sub(side, vec_scale(axis, U.Vector3.Dot(side, axis)))
            if side.sqrMagnitude < 1e-8:
                side = U.Vector3.Cross(axis, U.Vector3.forward)
            side.Normalize()
            depth = v3(sample.TargetReferenceFrame.Z)
            depth = vec_sub(depth, vec_scale(axis, U.Vector3.Dot(depth, axis)))
            if depth.sqrMagnitude > 1e-8:
                depth.Normalize()
            else:
                depth = U.Vector3.zero
            # Focus on the anatomical entry.  The wider opposite-side offset keeps
            # the camera outside both characters while the X-ray preview remains
            # legible over the in-world pose.
            self.side_focus = v3(sample.Target)
            self.side_position = vec_sub(
                vec_sub(self.side_focus, vec_scale(side, 0.900)),
                vec_scale(depth, 0.260))
            self.side_position = vec_add(self.side_position, U.Vector3(0.0, 0.120, 0.0))
            self.side_rotation = U.Quaternion.LookRotation(
                vec_sub(self.side_focus, self.side_position).normalized, axis)
            report['fixed_contact_camera'] = {
                'position': xyz(self.side_position),
                'rotation_xyzw': xyzw(self.side_rotation),
                'focus': xyz(self.side_focus),
                'fov': 34.0,
            }

        def prepare_frame(self, at):
            self.progress(prepare='candidate_start')
            candidate = self.candidate()
            self.progress(prepare='candidate_ready')
            sample = member(candidate, 'Native')
            self.progress(prepare='native_solve_start')
            native = typ('KoiSync.Core.NativeMotion').GetMethod('Solve', F).Invoke(
                None, System.Array[System.Object]([sample]))
            axes = [float(value) for value in member(native, 'Normalized')]
            raw = [float(value) for value in member(native, 'Raw')]
            self.progress(prepare='native_solve_ready')
            self.progress(prepare='display_start')
            invoke(self.preview, 'Display', [candidate, member(self.host, 'Pipeline'), candidate,
                                            System.Boolean(True)])
            self.progress(prepare='display_ready')
            if not bool(member(self.preview, 'AssetsLoaded')):
                raise Exception('KoiSync preview zipmod assets did not load')

            sleeve = member(self.preview, 'sleeveInstance')
            rod = member(self.preview, 'rodInstance')
            pose = member(self.preview, 'pose')
            if sleeve is None or rod is None or not sleeve.activeInHierarchy or not rod.activeInHierarchy:
                raise Exception('Preview instances are not visible')
            if pose is None:
                raise Exception('Native contact pose was not reconstructed')
            self.progress(prepare='layer_check_start')
            sleeve_layers = sorted(set(int(node.gameObject.layer) for node in
                                       sleeve.GetComponentsInChildren[U.Transform](True)))
            rod_layers = sorted(set(int(node.gameObject.layer) for node in
                                    rod.GetComponentsInChildren[U.Transform](True)))
            self.progress(prepare='layer_check_ready')
            if sleeve_layers != [10] or rod_layers != [10]:
                raise Exception('Preview layer mismatch: sleeve=%s rod=%s' %
                                (sleeve_layers, rod_layers))

            self.progress(prepare='camera_start')
            camera = studio.cameraCtrl.mainCmaera
            self.progress(prepare='camera_ready')
            stamp = int(round(at * 1000.0))
            prefix = OUT + ('frame_%02d_%06dms' % (self.index + 1, stamp))
            context_path = prefix + '_context.png'
            contact_path = prefix + '_contact.png'
            frame = {
                'index': self.index + 1,
                'time': at,
                'phase': ('detached' if self.index == 0 else
                          'approach' if self.index < 4 else
                          'first_insertion' if self.index == 4 else
                          'deep_insertion'),
                'native_axes_normalized': axes,
                'native_axes_percent': [int(round(value * 100.0)) for value in axes],
                'preview_axes_normalized': [float(value) for value in member(pose, 'Axes')],
                'native_raw': raw,
                'source_id': unicode(member(candidate, 'Id')),
                'frame_label': unicode(sample.FrameLabel),
                'native_axes_ppm': [int(round(value * 1000000.0)) for value in axes],
                'preview_axes_ppm': [int(round(float(value) * 1000000.0)) for value in member(pose, 'Axes')],
                'pipeline_desired': [float(v) for v in member(member(self.host, 'Pipeline'), 'Desired')],
                'pipeline_values': [float(v) for v in member(member(self.host, 'Pipeline'), 'Values')],
                'preview_status': unicode(member(self.preview, 'Status')),
                'material_insertion': float(member(native, 'MaterialInsertion')),
                'target_depth_m': float(member(native, 'TargetDepth')),
                'device_axis': [float(native.X.X), float(native.X.Y), float(native.X.Z)],
                'entry_error_um': int(round(U.Vector3.Distance(sleeve.transform.Find('Entry').position, v3(sample.TargetReferenceFrame.Origin)) * 1000000.0)),
                'neutral_axis_angle_deg': float(U.Vector3.Angle(v3(pose.RodDirection), v3(pose.Sleeve.Y))),
                'hardware_connected': bool(member(member(self.host, 'Devices'), 'Connected')),
                'engagement': float(member(native, 'Engagement')),
                'contact_distance_m': float(member(native, 'ContactDistance')),
                'calibrated_rod_length_m': float(member(pose, 'RodLength')),
                'visible_bp_length_m': float(member(pose, 'VisibleRodLength')),
                'rod_tip_to_game_entry_m': float(U.Vector3.Distance(
                    rod.transform.Find('RodEnd').position, v3(sample.Target))),
                'sleeve_entry_to_rod_tip_m': float(U.Vector3.Distance(
                    sleeve.transform.Find('Entry').position,
                    rod.transform.Find('RodEnd').position)),
                'sleeve_entry_to_game_entry_m': float(U.Vector3.Distance(
                    sleeve.transform.Find('Entry').position, v3(sample.Target))),
                'sleeve_channel_length_m': float(
                    member(self.preview, 'sleeveAssetLength') *
                    abs(sleeve.transform.localScale.y)),
                'sleeve_layers': sleeve_layers,
                'rod_layers': rod_layers,
                'context_path': context_path,
                'contact_path': contact_path,
                'capture_status': 'pending',
            }
            self.progress(prepare='record_save_start')
            report['frames'].append(frame)
            save(REPORT, report)
            self.progress(prepare='frame_recorded')
            self.frame_camera = camera
            self.frame_sample = sample
            self.frame_drive = None
            self.frame_sleeve = sleeve
            self.frame_rod = rod
            self.frame_context_path = context_path
            self.frame_contact_path = contact_path
            self.frame_record = frame

        def capture_context(self):
            # Isolated diagnostic camera renders the actual world-space preview
            # and entrance marker, without room occlusion or character meshes.
            self.frame_record['capture_status'] = 'diagnostic_pending'

        def capture_contact(self):
            camera_object = U.GameObject('KoiSync diagnostic camera')
            camera = camera_object.AddComponent[U.Camera]()
            marker = U.GameObject.CreatePrimitive(U.PrimitiveType.Sphere)
            marker.name = 'Measured target entry'
            marker.transform.position = v3(self.frame_sample.TargetReferenceFrame.Origin)
            marker.transform.localScale = U.Vector3(.006,.006,.006)
            marker.layer = 30
            material = marker.GetComponent[U.Renderer]().material
            material.shader = U.Shader.Find('Unlit/Color')
            material.color = U.Color(1,.85,.05,1)
            changed=[]
            try:
                for obj in (self.frame_sleeve, self.frame_rod):
                    for node in obj.GetComponentsInChildren[U.Transform](True):
                        changed.append((node.gameObject,int(node.gameObject.layer)))
                        node.gameObject.layer=30
                camera.enabled=False
                camera.cullingMask=1 << 30
                camera.clearFlags=U.CameraClearFlags.SolidColor
                camera.backgroundColor=U.Color(.035,.045,.065,1)
                camera.orthographic=True
                camera.orthographicSize=.24
                camera.nearClipPlane=.01
                camera.farClipPlane=3
                # Keep one camera for the complete cycle in actual game coordinates.
                if self.side_focus is None:
                    self.side_focus=vec_add(v3(self.frame_sample.Origin),U.Vector3(0,.14,0))
                position=vec_add(self.side_focus,U.Vector3(.48,.12,-.30))
                camera.transform.position=position
                camera.transform.rotation=U.Quaternion.LookRotation(vec_sub(self.side_focus,position),U.Vector3.up)
                capture(camera,self.frame_contact_path,900,900)
                self.frame_record['contact_bytes']=int(FileInfo(self.frame_contact_path).Length)
                self.frame_record['capture_status']='captured'
                save(REPORT,report)
            finally:
                for obj,layer in changed: obj.layer=layer
                U.Object.Destroy(marker)
                U.Object.Destroy(material)
                U.Object.Destroy(camera_object)

        def finish(self, status, error=None):
            try:
                U.AudioListener.pause = self.old_audio_pause
                if self.timeline_type is not None:
                    self.timeline_type.GetMethod('Pause', F).Invoke(None, None)
            except:
                pass
            report['status'] = status
            report['elapsed_realtime_seconds'] = float(U.Time.realtimeSinceStartup) - self.started
            report['hardware_connected'] = bool(member(member(self.host, 'Devices'), 'Connected')) if self.host is not None else None
            report['scene_saved'] = False
            if error is not None:
                report['error'] = error
            save(REPORT, report)
            save(PROGRESS, {
                'status': status,
                'phase': self.phase,
                'frames': len(report['frames']),
                'elapsed_realtime_seconds': report['elapsed_realtime_seconds'],
                'error': error,
            })

        def MoveNext(self):
            try:
                now = float(U.Time.realtimeSinceStartup)
                if self.phase == 'startup_wait':
                    self.progress(waiting_for_studio_seconds=20.0)
                    if now - self.started < 20.0:
                        # WaitForEndOfFrame can pause before Studio's first rendered scene.
                        # A null yield still advances one Unity Update without touching the UI.
                        self.current = None
                        return True
                    self.phase = 'load'

                if self.phase == 'load':
                    U.AudioListener.pause = True
                    self.progress()
                    self.host = U.Object.FindObjectOfType(typ('KoiSync.Plugin.Plugin'))
                    if self.host is None:
                        raise Exception('KoiSync.Plugin.Plugin was not found')
                    # Keep this large card out of the generic discovery pass.  The capture uses
                    # its already-verified object IDs and live BP chain directly below.
                    self.host.enabled = True
                    timeline_type = typ('Timeline.Timeline')
                    timeline = timeline_type.GetField('_self', F).GetValue(None)
                    tracks = (0 if timeline is None else
                              int(timeline_type.GetField('_interpolables', F)
                                  .GetValue(timeline).Count))
                    already_loaded = (tracks == 148 and 403 in studio.dicObjectCtrl and
                                      539 in studio.dicObjectCtrl)
                    report['reused_loaded_scene'] = already_loaded
                    if not already_loaded and not studio.LoadScene(CARD):
                        raise Exception('Studio.LoadScene returned false')
                    self.load_started = now - 8.0 if already_loaded else now
                    self.phase = 'wait_scene'
                    self.current = None
                    return True

                if self.phase == 'wait_scene':
                    self.timeline_type = typ('Timeline.Timeline')
                    self.timeline = self.timeline_type.GetField('_self', F).GetValue(None)
                    tracks = (0 if self.timeline is None else
                              int(self.timeline_type.GetField('_interpolables', F)
                                  .GetValue(self.timeline).Count))
                    ready = (tracks == 148 and 403 in studio.dicObjectCtrl and
                             539 in studio.dicObjectCtrl and now - self.load_started >= 8.0)
                    if not ready:
                        if now - self.load_started > 120.0:
                            raise Exception('v036 load timeout; tracks=%d objects=%d' %
                                            (tracks, int(studio.dicObjectCtrl.Count)))
                        self.current = None
                        return True
                    self.timeline_type.GetMethod('Pause', F).Invoke(None, None)
                    try:
                        from NodesConstraints import NodesConstraints
                        self.nodes = NodesConstraints()
                    except:
                        self.nodes = None
                    report['timeline_tracks'] = tracks
                    report['timeline_duration'] = float(
                        self.timeline_type.GetProperty('duration', F).GetValue(None, None))
                    self.configure_binding()
                    self.configure()
                    set_member(member(self.host, 'ScriptPlayback'), 'Enabled', False)
                    report['device_connected_before_capture'] = bool(
                        member(member(self.host, 'Devices'), 'Connected'))
                    if report['device_connected_before_capture']:
                        raise Exception('A hardware transport is unexpectedly connected')
                    self.phase = 'settle_configuration'
                    self.wait = 0
                    self.progress(timeline_tracks=tracks)
                    self.current = None
                    return True

                if self.phase == 'settle_configuration':
                    self.wait += 1
                    if self.wait < 90:
                        self.current = None
                        return True
                    self.phase = 'seek'

                if self.phase == 'seek':
                    if self.index >= len(TIMES):
                        self.seek(TIMES[-1])
                        self.phase = 'final_wait'
                        self.wait = 0
                        self.progress(left_at=TIMES[-1])
                        self.current = None
                        return True
                    self.seek(TIMES[self.index])
                    self.phase = 'settle_frame'
                    self.wait = 0
                    self.progress(seeking=TIMES[self.index])
                    self.current = None
                    return True

                if self.phase == 'settle_frame':
                    self.wait += 1
                    if self.wait < 24:
                        self.current = None
                        return True
                    self.prepare_frame(TIMES[self.index])
                    self.phase = 'capture_context'
                    self.progress(capture='context')
                    self.current = None
                    return True

                if self.phase == 'capture_context':
                    self.capture_context()
                    self.phase = 'capture_contact'
                    self.progress(capture='contact')
                    self.current = None
                    return True

                if self.phase == 'capture_contact':
                    self.capture_contact()
                    self.index += 1
                    self.phase = 'seek'
                    self.current = None
                    return True

                if self.phase == 'final_wait':
                    self.wait += 1
                    if self.wait < 6:
                        self.current = None
                        return True
                    self.finish('passed')
                    return False
                return False
            except:
                self.finish('error', traceback.format_exc())
                return False

    studio.StartCoroutine(Work())


coroutine.start_new_coroutine(run, (), {})
