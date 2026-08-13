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
const seenApprovalContainers = new WeakSet();
let wasGenerating = isGenerating();
let lastUrl = location.href;
let completionSequence = 0;

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

function sendActivity(status, activityId) {
  const url = getConversationUrl();
  if (!url) {
    return;
  }

  chrome.runtime.sendMessage({
    type: "codex-usage-tray-activity",
    activity: {
      status,
      activityId,
      url,
      title: getSafeTitle()
    }
  }).catch(() => {
    // The tray integration may not be installed yet; ChatGPT must keep working.
  });
}

function inspectPage() {
  if (location.href !== lastUrl) {
    lastUrl = location.href;
    wasGenerating = isGenerating();
  }

  const generating = isGenerating();
  if (wasGenerating && !generating) {
    completionSequence += 1;
    sendActivity("completed", `complete-${Date.now()}-${completionSequence}`);
  }
  wasGenerating = generating;

  if (findNewApprovalContainer()) {
    sendActivity("approval_required", `approval-${Date.now()}`);
  }
}

let inspectionScheduled = false;
const observer = new MutationObserver(() => {
  if (inspectionScheduled) {
    return;
  }

  inspectionScheduled = true;
  requestAnimationFrame(() => {
    inspectionScheduled = false;
    inspectPage();
  });
});

observer.observe(document.documentElement, {
  childList: true,
  subtree: true,
  attributes: true,
  attributeFilter: ["aria-label", "data-testid", "disabled"]
});
setInterval(inspectPage, 2000);
inspectPage();
