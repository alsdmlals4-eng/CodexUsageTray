"use strict";

const STOP_SELECTORS = [
  "button[data-testid='stop-button']",
  "button[aria-label='Stop streaming']",
  "button[aria-label='Stop generating']",
  "button[aria-label='응답 생성 중지']",
  "button[aria-label='생성 중지']"
];
const APPROVE_WORDS = ["approve", "allow", "confirm", "승인", "허용", "확인"];
const REJECT_WORDS = ["deny", "reject", "cancel", "거부", "취소"];
const RECOVERY_OUTCOME_GRACE_MS = 5000;
const seenApprovalContainers = new WeakSet();
const completionState = new CodexUsageTrayCompletionState.CompletionState(2000);
const recoveryWatchdog = new CodexUsageTrayRecoveryWatchdog.RecoveryWatchdog({
  stallMilliseconds: 180000,
  maxAttempts: 3
});
const recoveryController = new CodexUsageTrayRecoveryActionController.RecoveryActionController({
  sendActivity: sendRecoveryActivity
});
let completionSequence = 0;
let recoverySequence = 0;
let recoveryObservationBlockedUntil = 0;
let recoveryPendingRoute = null;
let monitoringStopped = false;
let monitoringIntervalId = null;

function stopMonitoring() {
  if (monitoringStopped) {
    return;
  }

  monitoringStopped = true;
  observer.disconnect();
  if (monitoringIntervalId !== null) {
    clearInterval(monitoringIntervalId);
    monitoringIntervalId = null;
  }
}

function getConversationUrl() {
  const url = new URL(location.href);
  const segments = url.pathname.split("/").filter(Boolean);
  const conversationMarker = segments.findIndex((segment) => segment === "c");
  const isConversation = conversationMarker >= 0 && segments[conversationMarker + 1];
  const isCodexTask = segments.length >= 2 && segments[0] === "codex";
  if (!isConversation && !isCodexTask) {
    return null;
  }

  url.search = "";
  url.hash = "";
  return url.toString();
}

function getRouteKey() {
  return getConversationUrl() || location.pathname;
}

function getSafeTitle() {
  return document.title
    .replace(/\s*[|\-–—]\s*ChatGPT\s*$/i, "")
    .trim()
    .slice(0, 80);
}

function isGenerating() {
  return STOP_SELECTORS.some((selector) => document.querySelector(selector));
}

function normalizedControlText(control) {
  return `${control.getAttribute("aria-label") || ""} ${control.textContent || ""}`
    .replace(/\s+/g, " ")
    .trim()
    .toLocaleLowerCase();
}

function hasAnyWord(value, words) {
  return words.some((word) => value.includes(word));
}

function findNewApprovalContainer() {
  const containers = document.querySelectorAll("[role='dialog'], form");
  for (const container of containers) {
    if (seenApprovalContainers.has(container)) {
      continue;
    }

    const controls = Array.from(container.querySelectorAll("button, [role='button']"));
    const labels = controls.map(normalizedControlText);
    const hasApproval = labels.some((label) => hasAnyWord(label, APPROVE_WORDS));
    const hasRejection = labels.some((label) => hasAnyWord(label, REJECT_WORDS));
    if (hasApproval && hasRejection) {
      seenApprovalContainers.add(container);
      return container;
    }
  }

  return null;
}

function sendActivity(status, activityId, metadata = {}) {
  if (monitoringStopped) {
    return;
  }

  const url = getConversationUrl();
  if (!url) {
    return;
  }

  const activity = {
    status,
    activityId,
    url,
    title: getSafeTitle()
  };
  if (typeof metadata.reason === "string" && metadata.reason) {
    activity.reason = metadata.reason;
  }
  if (Number.isInteger(metadata.attempt) && metadata.attempt > 0) {
    activity.attempt = metadata.attempt;
  }

  CodexUsageTrayRuntimeMessaging.sendRuntimeMessage(
    globalThis.chrome?.runtime,
    {
      type: "codex-usage-tray-activity",
      activity
    },
    stopMonitoring);
}

function sendRecoveryActivity(activity) {
  recoverySequence += 1;
  sendActivity(
    activity.status,
    `recovery-${Date.now()}-${recoverySequence}`,
    activity);
}

function findRecoveryState() {
  const candidate = CodexUsageTrayRecoveryDom.findSafeRecoveryCandidate(
    document,
    CodexUsageTrayRecoveryWatchdog.classifyTransientError);
  const surface = candidate?.surface ||
    CodexUsageTrayRecoveryDom.findTransientErrorSurface(
      document,
      CodexUsageTrayRecoveryWatchdog.classifyTransientError);
  return { candidate, surface };
}

function currentRecoveryCandidate() {
  return {
    routeKey: getRouteKey(),
    candidate: CodexUsageTrayRecoveryDom.findSafeRecoveryCandidate(
      document,
      CodexUsageTrayRecoveryWatchdog.classifyTransientError)
  };
}

function inspectRecovery({ now, generating, assistantMutated, routeKey }) {
  if (recoveryPendingRoute && recoveryPendingRoute !== routeKey) {
    recoveryPendingRoute = null;
    recoveryObservationBlockedUntil = 0;
  }

  if (recoveryPendingRoute === routeKey && (generating || assistantMutated)) {
    recoveryController.reportRecovered({
      routeKey,
      reason: "generation_resumed"
    });
    recoveryPendingRoute = null;
    recoveryObservationBlockedUntil = 0;
  }

  const { candidate, surface } = findRecoveryState();
  const transientError = Boolean(surface);
  const retryEvaluationBlocked = transientError &&
    Boolean(candidate) &&
    now < recoveryObservationBlockedUntil;
  if (retryEvaluationBlocked) {
    return;
  }

  const result = recoveryWatchdog.observe({
    now,
    routeKey,
    generating,
    assistantMutated,
    transientError,
    hasSafeRetryControl: Boolean(candidate)
  });

  if (result.action === "retry") {
    const accepted = recoveryController.requestRetry({
      routeKey,
      attempt: result.attempt,
      delayMilliseconds: result.delayMilliseconds,
      getCurrentCandidate: currentRecoveryCandidate
    });
    if (accepted) {
      recoveryPendingRoute = routeKey;
      recoveryObservationBlockedUntil = now +
        result.delayMilliseconds +
        RECOVERY_OUTCOME_GRACE_MS;
    }
    return;
  }

  if (result.action === "recovery_required") {
    const disconnectedWaiting = CodexUsageTrayRecoveryWatchdog.isDisconnectedWaiting(
      surface?.textContent || "");
    recoveryController.reportRecoveryRequired({
      routeKey,
      reason: disconnectedWaiting ? "disconnected_waiting" : result.reason
    });
  }
}

function inspectPage(assistantMutated = false) {
  if (monitoringStopped) {
    return;
  }

  const now = Date.now();
  const generating = isGenerating();
  const routeKey = getRouteKey();
  const result = completionState.observe({
    now,
    generating,
    assistantMutated,
    routeKey
  });

  inspectRecovery({ now, generating, assistantMutated, routeKey });

  if (result.completed) {
    if (recoveryPendingRoute === routeKey) {
      recoveryController.reportRecovered({
        routeKey,
        reason: "completed_after_recovery"
      });
      recoveryPendingRoute = null;
      recoveryObservationBlockedUntil = 0;
    }
    completionSequence += 1;
    sendActivity("completed", `complete-${now}-${completionSequence}`);
  }

  if (findNewApprovalContainer()) {
    sendActivity("approval_required", `approval-${now}`);
  }
}

function belongsToAssistantResponse(node) {
  const element = node instanceof Element ? node : node.parentElement;
  return Boolean(element?.closest("[data-message-author-role='assistant']"));
}

function mutationsTouchAssistantResponse(mutations) {
  return mutations.some((mutation) =>
    belongsToAssistantResponse(mutation.target) ||
    Array.from(mutation.addedNodes).some(belongsToAssistantResponse));
}

let inspectionScheduled = false;
let pendingAssistantMutation = false;
const observer = new MutationObserver((mutations) => {
  if (monitoringStopped) {
    return;
  }

  pendingAssistantMutation = pendingAssistantMutation ||
    mutationsTouchAssistantResponse(mutations);
  if (inspectionScheduled) {
    return;
  }

  inspectionScheduled = true;
  setTimeout(() => {
    if (monitoringStopped) {
      return;
    }

    inspectionScheduled = false;
    const assistantMutated = pendingAssistantMutation;
    pendingAssistantMutation = false;
    inspectPage(assistantMutated);
  }, 50);
});

observer.observe(document.documentElement, {
  childList: true,
  subtree: true,
  attributes: true,
  attributeFilter: ["aria-label", "data-testid", "disabled"]
});
monitoringIntervalId = setInterval(() => inspectPage(false), 500);
inspectPage(false);
