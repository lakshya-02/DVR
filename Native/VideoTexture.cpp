// Android's decoder writes to a SurfaceTexture. Copy its latest frame into the
// Unity screen texture on the render thread, where both eyes share its geometry.
#include <android/surface_texture_jni.h>
#include <android/log.h>
#include <GLES3/gl3.h>
#include <EGL/egl.h>
#include <GLES2/gl2ext.h>
#include <atomic>
#include <cstdint>

namespace {
struct Video {
    JavaVM* vm;
    jobject producerRef;
    ASurfaceTexture* producer;
    GLuint target;
    int width, height;
    EGLContext owner = EGL_NO_CONTEXT;
    GLuint external = 0, framebuffer = 0, program = 0, vao = 0;
    GLint transform = -1;
    int64_t timestamp = -1;
    bool attached = false;
    std::atomic<int> presented{0};
};

GLuint shader(GLenum type, const char* text) {
    GLuint name = glCreateShader(type);
    glShaderSource(name, 1, &text, nullptr);
    glCompileShader(name);
    GLint compiled;
    glGetShaderiv(name, GL_COMPILE_STATUS, &compiled);
    if (!compiled) {
        char reason[512] = {};
        glGetShaderInfoLog(name, sizeof(reason), nullptr, reason);
        __android_log_print(ANDROID_LOG_ERROR, "DvrVideo", "%s", reason);
        glDeleteShader(name);
        return 0;
    }
    return name;
}

bool initialize(Video& video) {
    video.owner = eglGetCurrentContext();
    glGenTextures(1, &video.external);
    if (ASurfaceTexture_attachToGLContext(video.producer, video.external) != 0) return false;
    video.attached = true;
    glBindTexture(GL_TEXTURE_EXTERNAL_OES, video.external);
    glTexParameteri(GL_TEXTURE_EXTERNAL_OES, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_EXTERNAL_OES, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    const char* vertex = R"(#version 300 es
uniform mat4 transform;
out vec2 uv;
void main() {
    vec2 corner = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
    uv = (transform * vec4(corner, 0.0, 1.0)).xy;
})";
    const char* fragment = R"(#version 300 es
#extension GL_OES_EGL_image_external_essl3 : require
precision highp float;
uniform samplerExternalOES frame;
in vec2 uv;
out vec4 pixel;
void main() {
    vec3 rgb = texture(frame, uv).rgb;
    pixel = vec4(mix(rgb/12.92, pow((rgb+0.055)/1.055, vec3(2.4)), step(vec3(0.04045), rgb)), 1.0);
})";
    GLuint vs = shader(GL_VERTEX_SHADER, vertex), fs = shader(GL_FRAGMENT_SHADER, fragment);
    video.program = glCreateProgram();
    if (vs) glAttachShader(video.program, vs);
    if (fs) glAttachShader(video.program, fs);
    glLinkProgram(video.program);
    glDeleteShader(vs);
    glDeleteShader(fs);
    GLint linked;
    glGetProgramiv(video.program, GL_LINK_STATUS, &linked);
    if (!linked) return false;
    video.transform = glGetUniformLocation(video.program, "transform");
    glGenVertexArrays(1, &video.vao);
    glGenFramebuffers(1, &video.framebuffer);
    glBindFramebuffer(GL_DRAW_FRAMEBUFFER, video.framebuffer);
    glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, video.target, 0);
    return glCheckFramebufferStatus(GL_DRAW_FRAMEBUFFER) == GL_FRAMEBUFFER_COMPLETE;
}

void render(int event, void* data) {
    Video& video = *static_cast<Video*>(data);
    if (event == 2) {
        if (video.owner != EGL_NO_CONTEXT && video.owner == eglGetCurrentContext()) {
            if (video.attached) ASurfaceTexture_detachFromGLContext(video.producer);
            else if (video.external) glDeleteTextures(1, &video.external);
            glDeleteFramebuffers(1, &video.framebuffer);
            glDeleteVertexArrays(1, &video.vao);
            glDeleteProgram(video.program);
        }
        ASurfaceTexture_release(video.producer);
        JNIEnv* env = nullptr;
        bool attach = video.vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) == JNI_EDETACHED;
        if (attach) video.vm->AttachCurrentThread(&env, nullptr);
        if (env) {
            jclass type = env->GetObjectClass(video.producerRef);
            env->CallVoidMethod(video.producerRef, env->GetMethodID(type, "release", "()V"));
            env->DeleteLocalRef(type);
            env->DeleteGlobalRef(video.producerRef);
        }
        if (attach) video.vm->DetachCurrentThread();
        delete &video;
        return;
    }
    if (video.owner != EGL_NO_CONTEXT && video.owner != eglGetCurrentContext()) video.presented.store(-1);
    if (video.presented.load() < 0) return;
    GLint active, external, framebuffer;
    glGetIntegerv(GL_ACTIVE_TEXTURE, &active);
    glActiveTexture(GL_TEXTURE0);
    glGetIntegerv(GL_TEXTURE_BINDING_EXTERNAL_OES, &external);
    glGetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &framebuffer);
    if ((!video.program && !initialize(video)) || ASurfaceTexture_updateTexImage(video.producer) != 0) {
        video.presented.store(-1);
        __android_log_print(ANDROID_LOG_ERROR, "DvrVideo", "Decoder surface or screen framebuffer unavailable");
    } else {
        int64_t timestamp = ASurfaceTexture_getTimestamp(video.producer);
        if (timestamp > 0 && timestamp != video.timestamp) {
            video.timestamp = timestamp;
            GLint viewport[4], program, vao, sampler;
            GLboolean mask[4];
            glGetIntegerv(GL_VIEWPORT, viewport);
            glGetIntegerv(GL_CURRENT_PROGRAM, &program);
            glGetIntegerv(GL_VERTEX_ARRAY_BINDING, &vao);
            glGetIntegerv(GL_SAMPLER_BINDING, &sampler);
            glGetBooleanv(GL_COLOR_WRITEMASK, mask);
            constexpr GLenum flags[] = {GL_SCISSOR_TEST, GL_BLEND, GL_DEPTH_TEST, GL_CULL_FACE, GL_STENCIL_TEST, GL_RASTERIZER_DISCARD};
            GLboolean enabled[6];
            for (int i=0; i<6; ++i) { enabled[i] = glIsEnabled(flags[i]); glDisable(flags[i]); }
            float matrix[16];
            ASurfaceTexture_getTransformMatrix(video.producer, matrix);
            glBindFramebuffer(GL_DRAW_FRAMEBUFFER, video.framebuffer);
            glViewport(0, 0, video.width, video.height);
            glColorMask(GL_TRUE, GL_TRUE, GL_TRUE, GL_TRUE);
            glUseProgram(video.program);
            glUniformMatrix4fv(video.transform, 1, GL_FALSE, matrix);
            glBindSampler(0, 0);
            glBindTexture(GL_TEXTURE_EXTERNAL_OES, video.external);
            glBindVertexArray(video.vao);
            glDrawArrays(GL_TRIANGLES, 0, 3);
            glBindVertexArray(vao);
            glBindSampler(0, sampler);
            glUseProgram(program);
            glColorMask(mask[0], mask[1], mask[2], mask[3]);
            glViewport(viewport[0], viewport[1], viewport[2], viewport[3]);
            for (int i=0; i<6; ++i) if (enabled[i]) glEnable(flags[i]);
            ++video.presented;
        }
    }
    glBindFramebuffer(GL_DRAW_FRAMEBUFFER, framebuffer);
    glBindTexture(GL_TEXTURE_EXTERNAL_OES, external);
    glActiveTexture(active);
}
}
extern "C" {
void* DvrCreateSurface(JavaVM* vm, jobject texture, void* target, int width, int height) {
    JNIEnv* env = nullptr;
    if (vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) != JNI_OK) return nullptr;
    ASurfaceTexture* surface = ASurfaceTexture_fromSurfaceTexture(env, texture);
    if (!surface) return nullptr;
    return new Video{vm, env->NewGlobalRef(texture), surface, static_cast<GLuint>(reinterpret_cast<uintptr_t>(target)), width, height};
}
int DvrPresentedFrames(Video* video) { return video->presented.load(); }
void* DvrRenderEvent() { return reinterpret_cast<void*>(render); }
}


