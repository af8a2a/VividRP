LOCAL_PATH := $(call my-dir)

LOCAL_SHORT_COMMANDS := true

include $(LOCAL_PATH)/Makefile.srcs

include $(CLEAR_VARS)
LOCAL_MODULE := mikktspace
LOCAL_C_INCLUDES += $(LOCAL_PATH)/../src
LOCAL_ARM_MODE := arm

LOCAL_SRC_FILES += $(SRCS)
LOCAL_LDLIBS := -llog
LOCAL_CFLAGS += -O3
LOCAL_CPPFLAGS += -O3

# build
include $(BUILD_SHARED_LIBRARY)